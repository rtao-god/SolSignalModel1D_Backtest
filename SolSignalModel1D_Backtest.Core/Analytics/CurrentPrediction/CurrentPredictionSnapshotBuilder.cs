using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Utils.Pnl;

namespace SolSignalModel1D_Backtest.Core.Analytics.CurrentPrediction
	{
	/// <summary>
	/// Единственная точка, где считается "текущий прогноз":
	/// - выбор последней записи;
	/// - forward 24h (из PredictionRecord);
	/// - торговые планы по всем политикам и веткам BASE/ANTI-D.
	/// Никакого вывода и I/O — только математика и формирование снимка.
	/// </summary>
	public static class CurrentPredictionSnapshotBuilder
		{
		public static CurrentPredictionSnapshot? Build (
			IReadOnlyList<PredictionRecord> records,
			IReadOnlyList<ILeveragePolicy> policies,
			double walletBalanceUsd )
			{
			if (records == null || records.Count == 0)
				return null;
			if (policies == null || policies.Count == 0)
				return null;

			// Берём последнюю запись по дате — как в старом коде.
			var last = records
				.OrderBy (r => r.DateUtc)
				.Last ();

			var snapshot = new CurrentPredictionSnapshot
				{
				GeneratedAtUtc = DateTime.UtcNow,
				PredictionDateUtc = last.DateUtc,
				PredLabel = last.PredLabel,
				PredLabelDisplay = FormatLabel (last),
				MicroDisplay = FormatMicro (last),
				RegimeDown = last.RegimeDown,
				SlProb = last.SlProb,
				SlHighDecision = last.SlHighDecision,
				Entry = last.Entry,
				MinMove = last.MinMove,
				Reason = last.Reason ?? string.Empty,
				WalletBalanceUsd = walletBalanceUsd
				};

			// Forward 24h уже заранее посчитан в PredictionRecord при построении records.
			if (last.MaxHigh24 > 0.0 && last.MinLow24 > 0.0 && last.Close24 > 0.0)
				{
				snapshot.Forward24h = new Forward24hSnapshot
					{
					MaxHigh = last.MaxHigh24,
					MinLow = last.MinLow24,
					Close = last.Close24
					};
				}

			// Для каждой политики строим две ветки: BASE и ANTI-D.
			foreach (var policy in policies)
				{
				AppendRowsForPolicy (snapshot, last, policy, walletBalanceUsd);
				}

			return snapshot;
			}

		private static void AppendRowsForPolicy (
			CurrentPredictionSnapshot snapshot,
			PredictionRecord rec,
			ILeveragePolicy policy,
			double walletBalanceUsd )
			{
			bool hasDir = TryGetDirection (rec, out var goLong, out _);
			bool isRiskDay = rec.SlHighDecision;

			double leverage = policy.ResolveLeverage (rec);
			// Оставляем старое поведение: имя берём из типа политики.
			string policyName = policy.GetType ().Name;

			// --- BASE branch ---
				{
				bool skipped = !hasDir || isRiskDay;

				var row = BuildRow (
					policyName,
					branch: "BASE",
					rec: rec,
					isRiskDay: isRiskDay,
					hasDirection: hasDir,
					skipped: skipped,
					goLong: goLong,
					leverage: leverage,
					walletBalanceUsd: walletBalanceUsd);

				snapshot.PolicyRows.Add (row);
				}

			// --- ANTI-D branch ---
				{
				bool skipped = !hasDir || !isRiskDay;

				var row = BuildRow (
					policyName,
					branch: "ANTI-D",
					rec: rec,
					isRiskDay: isRiskDay,
					hasDirection: hasDir,
					skipped: skipped,
					goLong: goLong,
					leverage: leverage,
					walletBalanceUsd: walletBalanceUsd);

				snapshot.PolicyRows.Add (row);
				}
			}

		private static CurrentPredictionPolicyRow BuildRow (
			string policyName,
			string branch,
			PredictionRecord rec,
			bool isRiskDay,
			bool hasDirection,
			bool skipped,
			bool goLong,
			double leverage,
			double walletBalanceUsd )
			{
			var row = new CurrentPredictionPolicyRow
				{
				PolicyName = policyName,
				Branch = branch,
				IsRiskDay = isRiskDay,
				HasDirection = hasDirection,
				Skipped = skipped,
				Direction = !hasDirection ? "-" : (goLong ? "LONG" : "SHORT"),
				Leverage = leverage,
				Entry = rec.Entry
				};

			// Если ветка активна — считаем план сделки.
			if (!skipped)
				{
				var plan = BuildTradePlan (rec, goLong, leverage, walletBalanceUsd);

				row.SlPct = plan.SlPct;
				row.TpPct = plan.TpPct;
				row.SlPrice = plan.SlPrice;
				row.TpPrice = plan.TpPrice;
				row.PositionUsd = plan.PositionUsd;
				row.PositionQty = plan.PositionQty;
				row.LiqPrice = plan.LiqPrice;
				row.LiqDistPct = plan.LiqDistPct;
				}

			return row;
			}

		private sealed class TradePlan
			{
			public double SlPct { get; init; }
			public double TpPct { get; init; }
			public double SlPrice { get; init; }
			public double TpPrice { get; init; }
			public double? PositionUsd { get; init; }
			public double? PositionQty { get; init; }
			public double? LiqPrice { get; init; }
			public double? LiqDistPct { get; init; }
			}

		/// <summary>
		/// Логика стопа/тейка и ликвидации — перенесена в builder из старого принтера.
		/// Здесь сосредоточена математика, чтобы не дублировать её в консоли и отчёте.
		/// </summary>
		private static TradePlan BuildTradePlan (
			PredictionRecord rec,
			bool goLong,
			double leverage,
			double walletBalanceUsd )
			{
			double entry = rec.Entry;
			if (entry <= 0.0)
				throw new InvalidOperationException ("Entry <= 0 — нельзя построить торговый план.");

			double baseMinMove = rec.MinMove > 0.0 ? rec.MinMove : 0.02;
			double slPct = baseMinMove;
			if (slPct < 0.01) slPct = 0.01;
			else if (slPct > 0.04) slPct = 0.04;

			double tpPct = slPct * 1.5;
			if (tpPct < 0.015) tpPct = 0.015;

			double slPrice = goLong
				? entry * (1.0 - slPct)
				: entry * (1.0 + slPct);

			double tpPrice = goLong
				? entry * (1.0 + tpPct)
				: entry * (1.0 - tpPct);

			double? posUsd = null;
			double? posQty = null;
			double? liqPrice = null;
			double? liqDistPct = null;

			if (walletBalanceUsd > 0.0)
				{
				posUsd = walletBalanceUsd * leverage;
				posQty = posUsd / entry;

				if (leverage > 1.0)
					{
					const double mmr = 0.004;

					if (goLong)
						{
						liqPrice = entry * (leverage - 1.0) / (leverage * (1.0 - mmr));
						}
					else
						{
						liqPrice = entry * (1.0 + leverage) / (leverage * (1.0 + mmr));
						}

					if (liqPrice.HasValue)
						{
						liqDistPct = goLong
							? (entry - liqPrice.Value) / entry * 100.0
							: (liqPrice.Value - entry) / entry * 100.0;
						}
					}
				}

			return new TradePlan
				{
				SlPct = slPct * 100.0,
				TpPct = tpPct * 100.0,
				SlPrice = slPrice,
				TpPrice = tpPrice,
				PositionUsd = posUsd,
				PositionQty = posQty,
				LiqPrice = liqPrice,
				LiqDistPct = liqDistPct
				};
			}

		private static bool TryGetDirection (
			PredictionRecord rec,
			out bool goLong,
			out bool goShort )
			{
			goLong = rec.PredLabel == 2 || (rec.PredLabel == 1 && rec.PredMicroUp);
			goShort = rec.PredLabel == 0 || (rec.PredLabel == 1 && rec.PredMicroDown);
			return goLong || goShort;
			}

		private static string FormatLabel ( PredictionRecord r )
			{
			return r.PredLabel switch
				{
					0 => "0 (down)",
					1 => "1 (flat)",
					2 => "2 (up)",
					_ => r.PredLabel.ToString ()
					};
			}

		private static string FormatMicro ( PredictionRecord r )
			{
			if (r.PredLabel != 1) return "не используется (не flat)";
			if (r.PredMicroUp) return "micro UP";
			if (r.PredMicroDown) return "micro DOWN";
			return "—";
			}
		}
	}
