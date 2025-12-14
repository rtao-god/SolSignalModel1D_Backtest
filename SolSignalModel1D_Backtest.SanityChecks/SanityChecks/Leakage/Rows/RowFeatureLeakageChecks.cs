using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using CoreWindowing = SolSignalModel1D_Backtest.Core.Causal.Data.Windowing;

namespace SolSignalModel1D_Backtest.SanityChecks.SanityChecks.Leakage.Rows
	{
	/// <summary>
	/// Self-check на жёсткую утечку в RowBuilder:
	/// пытается найти фичи, которые по факту дублируют future-таргеты
	/// (SolFwd1, MaxHigh24/MinLow24/Close24, Future1mAfterExit).
	///
	/// При обнаружении:
	/// - выставляет Success = false;
	/// - пишет в Errors конкретные даты, индексы фичей и имя future-таргета.
	/// </summary>
	public static class RowFeatureLeakageChecks
		{
		/// <summary>
		/// Любое значение по модулю ниже этого порога считаем "практически нулём"
		/// для целей фильтрации тривиальных совпадений.
		/// </summary>
		private const double ZeroValueTol = 1e-4; // 0.01 %

		/// <summary>
		/// Человекочитаемые имена фич по их индексам в RowBuilder.
		/// Важно: порядок должен совпадать с формированием feats в RowBuilder.
		/// </summary>
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
				"SolRsiCenteredNorm",// 11 (SolRsiCentered / 100)
				"RsiSlope3Norm",     // 12 (RsiSlope3 / 100)
				"GapBtcSol1",        // 13
				"GapBtcSol3",        // 14
				"IsDownRegime",      // 15 (0/1)
				"AtrPct",            // 16
				"DynVol",            // 17
				"IsStressRegime",    // 18 (0/1, hardRegime==2)
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
				Summary = "[rows-leak] RowBuilder features vs future targets"
				};

			var allRows = ctx.AllRows;
			var records = ctx.Records;
			var sol1m = ctx.Sol1m;
			var nyTz = ctx.NyTz;

			if (allRows == null || allRows.Count == 0)
				{
				return SelfCheckResult.Ok ("[rows-leak] skip: AllRows is empty.");
				}

			var totalRows = allRows.Count;

			// Records/1m могут быть null — в этом случае проверяем только SolFwd1.
			Dictionary<DateTime, BacktestRecord>? recByDate = null;
			if (records != null && records.Count > 0)
				{
				recByDate = records
					.GroupBy (r => r.DateUtc)
					.ToDictionary (g => g.Key, g => g.First ());
				}

			List<Candle1m>? sol1mSorted = null;
			if (sol1m != null && sol1m.Count > 0)
				{
				// Для поиска первой минуты после exit упорядочиваем по времени.
				sol1mSorted = sol1m
					.OrderBy (c => c.OpenTimeUtc)
					.ToList ();
				}

			// Собираем для каждого дня набор future-таргетов:
			// - SolFwd1 (из BacktestRecord);
			// - MaxHigh24 / MinLow24 / Close24 (из PredictionRecord, если есть);
			// - Future1mAfterExit (первая 1m после baseline-exit, если есть sol1m).
			var futureTargetsByDate = new Dictionary<DateTime, List<(string Name, double Value)>> ();

			foreach (var row in allRows)
				{
				var date = row.Causal.DateUtc;

				var targets = new List<(string Name, double Value)>
				{
					("SolFwd1", row.SolFwd1)
				};

				if (recByDate != null && recByDate.TryGetValue (date, out var rec))
					{
					targets.Add (("MaxHigh24", rec.MaxHigh24));
					targets.Add (("MinLow24", rec.MinLow24));
					targets.Add (("Close24", rec.Close24));
					}

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

			// Ищем почти-равенство фичей и future-таргетов.
			// Сохраняем сразу дополнительный контекст по строке —
			// чтобы в логах не гадать, что происходит.
			var suspiciousMatches = new List<MatchInfo> ();

			foreach (var row in allRows)
				{
				if (row.Causal.Features == null || row.Causal.Features.Length == 0)
					continue;

				if (!futureTargetsByDate.TryGetValue (row.Causal.DateUtc, out var targets) || targets.Count == 0)
					continue;

				var feats = row.Causal.Features;

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
								Date = row.Causal.DateUtc,
								FeatureIndex = fi,
								TargetName = name,
								FeatureVal = fVal,
								TargetVal = tVal,
								Label = row.Forward.TrueLabel,
								MinMove = row.MinMove,
								RegimeDown = row.RegimeDown,
								HardRegime = row.HardRegime,
								IsMorning = row.Causal.IsMorning
								});
							}
						}
					}
				}

			if (suspiciousMatches.Count == 0)
				{
				result.Summary += " → no suspicious feature/target equality detected.";
				return result;
				}

			// Базовые пороги:
			// - минимум совпадений;
			// - минимум доля по выборке;
			// - минимум ненулевых совпадений.
			const int MinMatchesPerFeatureTarget = 5;        // оставляем низким, но дальше классифицируем на noise/real
			const double MinMatchFrac = 0.005;               // ≥0.5% выборки
			const int MinNonZeroMatchesPerFeatureTarget = 3; // минимум ненулевых совпадений

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

				// Классический случай шума: бинарная фича (0/1) vs почти везде SolFwd1≈0.
				if (isBinaryFeature && targetMostlyZero && g.TargetName == "SolFwd1")
					{
					noiseGroups.Add (g);
					}
				else
					{
					realLeaks.Add (g);
					}
				}

			// Сначала логируем «шумные» группы как предупреждения:
			foreach (var g in noiseGroups)
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
					$"[rows-leak] noise group (binary/near-zero): " +
					$"featureIndex={g.FeatureIndex} ({featName}), " +
					$"target={g.TargetName}, matches={countTotal}, " +
					$"frac={fracTotal:P2}, nonZero={countNonZero}, fracNonZero={fracNonZero:P2}";

				result.Warnings.Add (header);

				foreach (var sample in g.AllMatches.OrderBy (x => x.Causal.DateUtc).Take (5))
					{
					var line =
						$"[rows-leak:noise]   date={sample.Causal.DateUtc:O}, " +
						$"featureVal={sample.FeatureVal:0.########}, " +
						$"targetVal={sample.TargetVal:0.########}, " +
						$"label={sample.Forward.TrueLabel}, minMove={sample.MinMove:0.####}, " +
						$"regimeDown={sample.RegimeDown}, hardRegime={sample.HardRegime}, isMorning={sample.Causal.IsMorning}";
					result.Warnings.Add (line);
					}
				}

			if (realLeaks.Count == 0)
				{
				// Есть только шумные группы (как у тебя с feat 15/18):
				// трактуем как отсутствие жёстких утечек.
				result.Summary += " → only binary/near-zero groups, treated as noise.";
				result.Success = true;
				return result;
				}

			// Если остались реальные подозрения → валим self-check
			result.Success = false;

			foreach (var g in realLeaks.OrderByDescending (x => x.NonZeroMatches.Count))
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
					$"[rows-leak] possible feature leak: featureIndex={g.FeatureIndex} ({featName}), " +
					$"target={g.TargetName}, matches={countTotal}, " +
					$"frac={fracTotal:P2}, nonZero={countNonZero}, fracNonZero={fracNonZero:P2}";

				result.Errors.Add (header);
				result.Metrics[$"rows.leak.{g.TargetName}.feat{g.FeatureIndex}.matchesTotal"] = countTotal;
				result.Metrics[$"rows.leak.{g.TargetName}.feat{g.FeatureIndex}.matchesNonZero"] = countNonZero;
				result.Metrics[$"rows.leak.{g.TargetName}.feat{g.FeatureIndex}.fracTotal"] = fracTotal;
				result.Metrics[$"rows.leak.{g.TargetName}.feat{g.FeatureIndex}.fracNonZero"] = fracNonZero;

				foreach (var sample in g.NonZeroMatches.OrderBy (x => x.Causal.DateUtc).Take (5))
					{
					var line =
						$"[rows-leak]   date={sample.Causal.DateUtc:O}, " +
						$"featureVal={sample.FeatureVal:0.########}, " +
						$"targetVal={sample.TargetVal:0.########}, " +
						$"label={sample.Forward.TrueLabel}, minMove={sample.MinMove:0.####}, " +
						$"regimeDown={sample.RegimeDown}, hardRegime={sample.HardRegime}, isMorning={sample.Causal.IsMorning}";
					result.Errors.Add (line);
					}
				}

			return result;
			}

		/// <summary>
		/// Поиск первой 1m-свечи строго после указанного времени.
		/// </summary>
		private static Candle1m? FindFirstMinuteAfter ( List<Candle1m> minutes, DateTime t )
			{
			for (int i = 0; i < minutes.Count; i++)
				{
				if (minutes[i].OpenTimeUtc > t)
					return minutes[i];
				}
			return null;
			}

		/// <summary>
		/// Проверка почти-равенства двух double значений:
		/// сначала по абсолютному порогу, затем по относительному.
		/// </summary>
		private static bool IsNearlyEqual ( double x, double y )
			{
			if (double.IsNaN (x) || double.IsNaN (y))
				return false;

			const double AbsTol = 1e-8;   // для значений около 0
			const double RelTol = 1e-4;   // 0.01% относительная погрешность

			double diff = Math.Abs (x - y);
			if (diff <= AbsTol)
				return true;

			double max = Math.Max (Math.Abs (x), Math.Abs (y));
			if (max == 0.0)
				return diff == 0.0;

			return diff / max <= RelTol;
			}

		/// <summary>
		/// Одна конкретная "подозрительная" пара (фича ↔ таргет) для конкретной даты.
		/// </summary>
		private sealed class MatchInfo
			{
			public DateTime Date { get; set; }
			public int FeatureIndex { get; set; }
			public string TargetName { get; set; } = string.Empty;
			public double FeatureVal { get; set; }
			public double TargetVal { get; set; }
			public int Label { get; set; }
			public double MinMove { get; set; }
			public bool RegimeDown { get; set; }
			public int HardRegime { get; set; }
			public bool IsMorning { get; set; }
			}

		/// <summary>
		/// Группа совпадений по конкретной паре (featureIndex, targetName).
		/// </summary>
		private sealed class FeatureTargetGroup
			{
			public int FeatureIndex { get; set; }
			public string TargetName { get; set; } = string.Empty;
			public List<MatchInfo> AllMatches { get; set; } = new ();
			public List<MatchInfo> NonZeroMatches { get; set; } = new ();
			}
		}
	}
