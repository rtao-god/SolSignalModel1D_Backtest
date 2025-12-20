using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using CoreWindowing = SolSignalModel1D_Backtest.Core.Causal.Time.Windowing;

namespace SolSignalModel1D_Backtest.SanityChecks.SanityChecks.Leakage.Rows
	{
	/// <summary>
	/// Self-check на жёсткую утечку: пытается найти фичи, которые численно совпадают
	/// с будущими таргетами (MaxHigh24/MinLow24/Close24/первая 1m после exit и т.п.).
	///
	/// Ключевой принцип: фичи читаем только из каузального вектора (FeaturesVector),
	/// future-таргеты берём только из omniscient части BacktestRecord.
	/// </summary>
	public static class RowFeatureLeakageChecks
		{
		private const double ZeroValueTol = 1e-4; // 0.01 %

		private static readonly string[] FeatureNames =
			{
				"SolRet30",          //  0
				"BtcRet30",          //  1
				"SolBtcRet30",       //  2
				"SolRet1",           //  3
				"SolRet3",           //  4
				"BtcRet1",           //  5
				"BtcRet3",           //  6
				"FngNorm",           //  7
				"DxyChg30",          //  8
				"GoldChg30",         //  9
				"BtcVs200",          // 10
				"SolRsiCenteredNorm",// 11
				"RsiSlope3Norm",     // 12
				"GapBtcSol1",        // 13
				"GapBtcSol3",        // 14
				"IsDownRegime",      // 15
				"AtrPct",            // 16
				"DynVol",            // 17
				"IsStressRegime",    // 18
				"SolAboveEma50",     // 19
				"SolEma50vs200",     // 20
				"BtcEma50vs200"      // 21
			};

		public static SelfCheckResult CheckRowFeaturesAgainstFuture ( SelfCheckContext ctx )
			{
			if (ctx == null) throw new ArgumentNullException (nameof (ctx));

			var result = new SelfCheckResult
				{
				Success = true,
				Summary = "[rows-leak] features vs future targets"
				};

			// В текущей архитектуре “строки” для проверки — это BacktestRecord:
			// внутри есть Causal (вектор фич) и Forward (будущие факты).
			var allRows = ctx.Records;
			var sol1m = ctx.Sol1m;
			var nyTz = ctx.NyTz;

			if (allRows == null || allRows.Count == 0)
				{
				return SelfCheckResult.Ok ("[rows-leak] skip: AllRows is empty.");
				}

			List<Candle1m>? sol1mSorted = null;
			if (sol1m != null && sol1m.Count > 0)
				{
				sol1mSorted = sol1m
					.OrderBy (c => c.OpenTimeUtc)
					.ToList ();
				}

			// Собираем future-таргеты на дату.
			var futureTargetsByDate = new Dictionary<DateTime, List<(string Name, double Value)>> ();

			foreach (var rec in allRows)
				{
				if (rec == null)
					throw new InvalidOperationException ("[rows-leak] AllRows contains null item.");

				var date = rec.ToCausalDateUtc ();

				var targets = new List<(string Name, double Value)> ();

				// MaxHigh24/MinLow24/Close24 — явные future-факты.
				targets.Add (("MaxHigh24", rec.MaxHigh24));
				targets.Add (("MinLow24", rec.MinLow24));
				targets.Add (("Close24", rec.Close24));

				// SolFwd1 как отдельного поля может не быть.
				// Если нужно сравнение с “доходностью” по суткам — считаем строго и явно.
				if (double.IsFinite (rec.Entry) && rec.Entry > 0.0 && double.IsFinite (rec.Close24))
					{
					double solFwd1 = rec.Close24 / rec.Entry - 1.0;
					targets.Add (("SolFwd1(calc:Close24/Entry-1)", solFwd1));
					}

				// Первая 1m после baseline-exit.
				if (sol1mSorted != null && nyTz != null)
					{
					var exitUtc = CoreWindowing.ComputeBaselineExitUtc (date, nyTz);
					var future1m = FindFirstMinuteAfter (sol1mSorted, exitUtc);
					if (future1m != null)
						{
						targets.Add (("Future1mAfterExit", future1m.Close));
						}
					}

				futureTargetsByDate[date] = targets;
				}

			var suspiciousMatches = new List<MatchInfo> (capacity: 256);

			foreach (var rec in allRows)
				{
				if (rec == null) continue;

				var date = rec.ToCausalDateUtc ();

				if (!futureTargetsByDate.TryGetValue (date, out var targets) || targets.Count == 0)
					continue;

				// Фичи берём ТОЛЬКО из каузального вектора.
				var vec = rec.Causal.FeaturesVector;
				if (vec.IsEmpty)
					continue;

				var feats = vec.Span;

				for (int fi = 0; fi < feats.Length; fi++)
					{
					double fVal = feats[fi];
					if (double.IsNaN (fVal) || double.IsInfinity (fVal))
						continue;

					foreach (var (name, tVal) in targets)
						{
						if (double.IsNaN (tVal) || double.IsInfinity (tVal))
							continue;

						if (IsNearlyEqual (fVal, tVal))
							{
							suspiciousMatches.Add (new MatchInfo
								{
								Date = date,
								FeatureIndex = fi,
								TargetName = name,
								FeatureVal = fVal,
								TargetVal = tVal,

								// Диагностический контекст. Не все поля обязаны существовать в BacktestRecord —
								// здесь используем только то, что уже проксируется/есть в текущей модели.
								TrueLabel = rec.TrueLabel,
								MinMove = rec.MinMove,
								RegimeDown = rec.RegimeDown,
								IsMorning = rec.Causal.IsMorning == true
								});
							}
						}
					}
				}

			if (suspiciousMatches.Count == 0)
				{
				result.Summary += " → no suspicious equality detected.";
				return result;
				}

			const int MinMatchesPerFeatureTarget = 5;
			const double MinMatchFrac = 0.005;               // ≥0.5% выборки
			const int MinNonZeroMatchesPerFeatureTarget = 3; // минимум ненулевых совпадений

			int totalRows = allRows.Count;

			var groupedRaw = suspiciousMatches
				.GroupBy (m => new { m.FeatureIndex, m.TargetName })
				.Select (g =>
				{
					var all = g.ToList ();
					var nonZero = all
						.Where (m =>
							Math.Abs (m.FeatureVal) > ZeroValueTol ||
							Math.Abs (m.TargetVal) > ZeroValueTol)
						.ToList ();

					return new FeatureTargetGroup
						{
						FeatureIndex = g.Key.FeatureIndex,
						TargetName = g.Key.TargetName,
						AllMatches = all,
						NonZeroMatches = nonZero
						};
				})
				.Where (g =>
					g.AllMatches.Count >= MinMatchesPerFeatureTarget &&
					g.AllMatches.Count / (double) totalRows >= MinMatchFrac &&
					g.NonZeroMatches.Count >= MinNonZeroMatchesPerFeatureTarget)
				.ToList ();

			if (groupedRaw.Count == 0)
				{
				result.Summary += " → only rare coincidences, treating as noise.";
				return result;
				}

			var realLeaks = new List<FeatureTargetGroup> ();
			var noiseGroups = new List<FeatureTargetGroup> ();

			foreach (var g in groupedRaw)
				{
				bool isBinaryFeature =
					g.AllMatches.All (m =>
						Math.Abs (m.FeatureVal) <= 1.0 + 1e-6 &&
						(Math.Abs (m.FeatureVal) <= ZeroValueTol ||
						 Math.Abs (m.FeatureVal - 1.0) <= ZeroValueTol));

				double fracZeroTarget =
					g.AllMatches.Count (m => Math.Abs (m.TargetVal) <= ZeroValueTol)
					/ (double) g.AllMatches.Count;

				bool targetMostlyZero = fracZeroTarget >= 0.8;

				// Типовой шум: бинарная фича vs таргет, который почти всегда около 0.
				if (isBinaryFeature && targetMostlyZero)
					noiseGroups.Add (g);
				else
					realLeaks.Add (g);
				}

			foreach (var g in noiseGroups)
				{
				LogGroup (result.Warnings, totalRows, g, "[rows-leak] noise group");
				}

			if (realLeaks.Count == 0)
				{
				result.Summary += " → only binary/near-zero groups, treated as noise.";
				result.Success = true;
				return result;
				}

			result.Success = false;
			foreach (var g in realLeaks.OrderByDescending (x => x.NonZeroMatches.Count))
				{
				LogGroup (result.Errors, totalRows, g, "[rows-leak] possible feature leak");
				}

			result.Summary += $" → FAILED: suspicious groups={realLeaks.Count}.";
			return result;
			}

		private static void LogGroup ( List<string> sink, int totalRows, FeatureTargetGroup g, string headerPrefix )
			{
			int countTotal = g.AllMatches.Count;
			int countNonZero = g.NonZeroMatches.Count;
			double fracTotal = countTotal / (double) totalRows;
			double fracNonZero = countNonZero / (double) totalRows;

			string featName =
				g.FeatureIndex >= 0 && g.FeatureIndex < FeatureNames.Length
					? FeatureNames[g.FeatureIndex]
					: $"feat{g.FeatureIndex}";

			var header =
				$"{headerPrefix}: featureIndex={g.FeatureIndex} ({featName}), " +
				$"target={g.TargetName}, matches={countTotal}, frac={fracTotal:P2}, " +
				$"nonZero={countNonZero}, fracNonZero={fracNonZero:P2}";

			sink.Add (header);

			foreach (var sample in g.NonZeroMatches.OrderBy (x => x.Date).Take (5))
				{
				var line =
					$"[rows-leak]   date={sample.Date:O}, " +
					$"featureVal={sample.FeatureVal:0.########}, " +
					$"targetVal={sample.TargetVal:0.########}, " +
					$"trueLabel={sample.TrueLabel}, minMove={sample.MinMove:0.####}, " +
					$"regimeDown={sample.RegimeDown}, isMorning={sample.IsMorning}";
				sink.Add (line);
				}
			}

		private static Candle1m? FindFirstMinuteAfter ( List<Candle1m> minutes, DateTime t )
			{
			for (int i = 0; i < minutes.Count; i++)
				{
				if (minutes[i].OpenTimeUtc > t)
					return minutes[i];
				}
			return null;
			}

		private static bool IsNearlyEqual ( double x, double y )
			{
			if (double.IsNaN (x) || double.IsNaN (y))
				return false;

			const double AbsTol = 1e-8;
			const double RelTol = 1e-4;

			double diff = Math.Abs (x - y);
			if (diff <= AbsTol)
				return true;

			double max = Math.Max (Math.Abs (x), Math.Abs (y));
			if (max == 0.0)
				return diff == 0.0;

			return diff / max <= RelTol;
			}

		private sealed class MatchInfo
			{
			public DateTime Date { get; set; }
			public int FeatureIndex { get; set; }
			public string TargetName { get; set; } = string.Empty;
			public double FeatureVal { get; set; }
			public double TargetVal { get; set; }

			public int TrueLabel { get; set; }
			public double MinMove { get; set; }
			public bool RegimeDown { get; set; }
			public bool IsMorning { get; set; }
			}

		private sealed class FeatureTargetGroup
			{
			public int FeatureIndex { get; set; }
			public string TargetName { get; set; } = string.Empty;
			public List<MatchInfo> AllMatches { get; set; } = new ();
			public List<MatchInfo> NonZeroMatches { get; set; } = new ();
			}
		}
	}
