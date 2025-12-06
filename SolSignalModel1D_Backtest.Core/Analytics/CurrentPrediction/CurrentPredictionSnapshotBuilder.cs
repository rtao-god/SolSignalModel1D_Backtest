using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Utils.Pnl;

namespace SolSignalModel1D_Backtest.Core.Analytics.CurrentPrediction
	{
	/// <summary>
	/// Единственная точка, где считается "текущий прогноз":
	/// - выбор последней записи;
	/// - forward 24h (из PredictionRecord);
	/// - торговые планы по всем политикам и веткам BASE/ANTI-D.
	///
	/// Семантика веток:
	/// - BASE: торгует только нерискованные дни по направлению дневной модели;
	/// - ANTI-D: берёт только рискованные дни и переворачивает направление (LONG→SHORT, SHORT→LONG).
	/// </summary>
	public static class CurrentPredictionSnapshotBuilder
		{
		/// <summary>
		/// Размер окна истории (в днях) для бэкфилла "текущего прогноза".
		/// Это константа по умолчанию, которую удобно править руками.
		/// </summary>
		public const int DefaultHistoryWindowDays = 60;

		/// <summary>
		/// "Текущий" прогноз: берётся последняя PredictionRecord по времени.
		/// Старое поведение сохранено, но реализация вынесена в BuildFromRecord.
		/// </summary>
		public static CurrentPredictionSnapshot Build (
			IReadOnlyList<PredictionRecord> records,
			IReadOnlyList<ILeveragePolicy> policies,
			double walletBalanceUsd )
			{
			if (records == null)
				throw new ArgumentNullException (
					nameof (records),
					"[current] records == null при построении CurrentPredictionSnapshot — нарушен инвариант пайплайна");

			if (records.Count == 0)
				throw new InvalidOperationException (
					"[current] records пустой при построении CurrentPredictionSnapshot — нет ни одной PredictionRecord");

			// Берётся последняя по времени запись модели — это "текущий" прогноз.
			var last = records
				.OrderBy (r => r.DateUtc)
				.Last ();

			return BuildFromRecord (last, policies, walletBalanceUsd);
			}

		/// <summary>
		/// Строит снапшот "текущего прогноза" по конкретной PredictionRecord.
		/// Это кирпичик, который можно переиспользовать как для "текущего" дня,
		/// так и для исторических дат.
		/// </summary>
		public static CurrentPredictionSnapshot BuildFromRecord (
			PredictionRecord rec,
			IReadOnlyList<ILeveragePolicy> policies,
			double walletBalanceUsd )
			{
			if (rec == null)
				throw new ArgumentNullException (
					nameof (rec),
					"[current] rec == null при построении CurrentPredictionSnapshot — нарушен инвариант пайплайна");

			if (policies == null)
				throw new ArgumentNullException (
					nameof (policies),
					"[current] policies == null при построении CurrentPredictionSnapshot");

			if (policies.Count == 0)
				throw new InvalidOperationException (
					"[current] список политик пуст при построении CurrentPredictionSnapshot");

			var snapshot = new CurrentPredictionSnapshot
				{
				GeneratedAtUtc = DateTime.UtcNow,
				PredictionDateUtc = rec.DateUtc,
				PredLabel = rec.PredLabel,
				PredLabelDisplay = FormatLabel (rec),
				MicroDisplay = FormatMicro (rec),
				RegimeDown = rec.RegimeDown,
				SlProb = rec.SlProb,
				SlHighDecision = rec.SlHighDecision,
				Entry = rec.Entry,
				MinMove = rec.MinMove,
				Reason = rec.Reason ?? string.Empty,
				WalletBalanceUsd = walletBalanceUsd
				};

			// Forward 24h уже заранее посчитан в PredictionRecord при построении records.
			if (rec.MaxHigh24 > 0.0 && rec.MinLow24 > 0.0 && rec.Close24 > 0.0)
				{
				snapshot.Forward24h = new Forward24hSnapshot
					{
					MaxHigh = rec.MaxHigh24,
					MinLow = rec.MinLow24,
					Close = rec.Close24
					};
				}

			// Для каждой политики строятся две ветки: BASE и ANTI-D.
			foreach (var policy in policies)
				{
				AppendRowsForPolicy (snapshot, rec, policy, walletBalanceUsd);
				}

			// Объяснение прогноза поверх уже посчитанных полей снапшота.
			BuildExplanationItems (snapshot, rec);

			return snapshot;
			}

		/// <summary>
		/// Строит снапшоты для исторического окна по датам PredictionRecord.
		/// Окно задаётся в днях назад от текущего UTC-дня.
		/// Используется для бэкфилла "текущего прогноза" (например, за последние 60 дней).
		/// </summary>
		public static IReadOnlyList<CurrentPredictionSnapshot> BuildHistory (
			IReadOnlyList<PredictionRecord> records,
			IReadOnlyList<ILeveragePolicy> policies,
			double walletBalanceUsd,
			int historyWindowDays )
			{
			if (records == null)
				throw new ArgumentNullException (
					nameof (records),
					"[current] records == null при построении истории CurrentPredictionSnapshot");

			if (records.Count == 0)
				throw new InvalidOperationException (
					"[current] records пустой при построении истории CurrentPredictionSnapshot");

			if (historyWindowDays <= 0)
				throw new ArgumentOutOfRangeException (
					nameof (historyWindowDays),
					"[current] historyWindowDays должен быть > 0 при построении истории CurrentPredictionSnapshot");

			var cutoffUtc = DateTime.UtcNow.Date.AddDays (-historyWindowDays);

			var ordered = records
				.Where (r => r.DateUtc >= cutoffUtc)
				.OrderBy (r => r.DateUtc)
				.ToList ();

			var result = new List<CurrentPredictionSnapshot> (ordered.Count);

			foreach (var rec in ordered)
				{
				var snapshot = BuildFromRecord (rec, policies, walletBalanceUsd);
				result.Add (snapshot);
				}

			return result;
			}

		/// <summary>
		/// Строит снапшот для конкретной календарной даты (по UTC).
		/// Используется для запросов "покажи прогноз модели за такой-то день".
		/// </summary>
		public static CurrentPredictionSnapshot BuildForDate (
			IReadOnlyList<PredictionRecord> records,
			IReadOnlyList<ILeveragePolicy> policies,
			double walletBalanceUsd,
			DateTime predictionDateUtc )
			{
			if (records == null)
				throw new ArgumentNullException (
					nameof (records),
					"[current] records == null при построении CurrentPredictionSnapshot по дате");

			if (records.Count == 0)
				throw new InvalidOperationException (
					"[current] records пустой при построении CurrentPredictionSnapshot по дате");

			var targetDateUtc = predictionDateUtc.Date;

			var recForDay = records
				.OrderBy (r => r.DateUtc)
				.LastOrDefault (r => r.DateUtc.Date == targetDateUtc);

			if (recForDay == null)
				{
				throw new InvalidOperationException (
					$"[current] Не найден PredictionRecord для даты {targetDateUtc:yyyy-MM-dd} (UTC) при построении CurrentPredictionSnapshot.");
				}

			return BuildFromRecord (recForDay, policies, walletBalanceUsd);
			}

		/// <summary>
		/// Наполняет snapshot.ExplanationItems агрегированными причинами прогноза:
		/// дневная модель, микро-модель, SL-модель, режим, MinMove, baseline 24h и политики.
		/// Здесь НЕТ новой математической логики модели, только форматирование уже посчитанных полей.
		/// </summary>
		private static void BuildExplanationItems (
	CurrentPredictionSnapshot snapshot,
	PredictionRecord rec )
			{
			if (snapshot == null)
				throw new ArgumentNullException (nameof (snapshot));

			var items = snapshot.ExplanationItems;
			items.Clear ();

			int rank = 1;

			// 1. Дневная модель (основной класс)
			items.Add (new CurrentPredictionExplanationItem
				{
				Kind = "model",
				Name = "daily",
				Description = $"Дневная модель (Daily): класс {snapshot.PredLabelDisplay}",
				Rank = rank++
				});

			// 2. Микро-модель (1m), только если она реально участвует (label=1/flat)
			if (snapshot.PredLabel == 1 && !string.IsNullOrWhiteSpace (snapshot.MicroDisplay))
				{
				items.Add (new CurrentPredictionExplanationItem
					{
					Kind = "model",
					Name = "micro_1m",
					Description = $"Микро-модель (1m): {snapshot.MicroDisplay}",
					Rank = rank++
					});
				}

			// 3. SL-модель: вероятность стопа и HIGH/OK-решение
			items.Add (new CurrentPredictionExplanationItem
				{
				Kind = "model",
				Name = "sl",
				Description =
					$"SL-модель: вероятность стопа {snapshot.SlProb:0.0} %, " +
					$"решение = {(snapshot.SlHighDecision ? "HIGH (рискованный день)" : "OK (обычный день)")}",
				Value = snapshot.SlProb,
				Rank = rank++
				});

			// 4. Режим рынка (DOWN / NORMAL)
			items.Add (new CurrentPredictionExplanationItem
				{
				Kind = "model",
				Name = "regime",
				Description = snapshot.RegimeDown
					? "Режим рынка: DOWN (фаза снижения, защитный режим)"
					: "Режим рынка: NORMAL (обычный режим)",
				Rank = rank++
				});

			// 5. MinMove (адаптивный минимальный ход цены)
			// Переводим в отдельный тип, чтобы фронт отличал его от PFI-фич.
			items.Add (new CurrentPredictionExplanationItem
				{
				Kind = "metric",
				Name = "min_move",
				Description = $"MinMove: {snapshot.MinMove:0.0000} ({snapshot.MinMove * 100.0:0.0} %)",
				Value = snapshot.MinMove,
				Rank = rank++
				});

			// 6. Forward 24h baseline, если есть
			if (snapshot.Forward24h != null)
				{
				items.Add (new CurrentPredictionExplanationItem
					{
					Kind = "metric",
					Name = "forward_24h_max_high",
					Description = $"Baseline 24h MaxHigh: {snapshot.Forward24h.MaxHigh:0.0000}",
					Value = snapshot.Forward24h.MaxHigh,
					Rank = rank++
					});

				items.Add (new CurrentPredictionExplanationItem
					{
					Kind = "metric",
					Name = "forward_24h_min_low",
					Description = $"Baseline 24h MinLow: {snapshot.Forward24h.MinLow:0.0000}",
					Value = snapshot.Forward24h.MinLow,
					Rank = rank++
					});

				items.Add (new CurrentPredictionExplanationItem
					{
					Kind = "metric",
					Name = "forward_24h_close",
					Description = $"Baseline 24h Close: {snapshot.Forward24h.Close:0.0000}",
					Value = snapshot.Forward24h.Close,
					Rank = rank++
					});
				}

			// 7. Взаимодействие SL-модели и ветки ANTI-D
			// Если день рискованный и есть направление от дневной модели,
			// ANTI-D переворачивает направление (LONG↔SHORT).
			bool hasDirection = TryGetDirection (rec, out var goLongModel, out _);

			if (hasDirection && snapshot.SlHighDecision)
				{
				string baseDir = goLongModel ? "LONG" : "SHORT";
				string antiDir = goLongModel ? "SHORT" : "LONG";

				items.Add (new CurrentPredictionExplanationItem
					{
					Kind = "policy",
					Name = "anti_d_override",
					Description =
						"Ветка ANTI-D активна: базовая дневная модель даёт " + baseDir +
						", SL-модель пометила день как рискованный → ветка ANTI-D " +
						"торгует в обратную сторону (" + antiDir + ").",
					Rank = rank++
					});
				}
			}

		private static void AppendRowsForPolicy (
			CurrentPredictionSnapshot snapshot,
			PredictionRecord rec,
			ILeveragePolicy policy,
			double walletBalanceUsd )
			{
			// hasDir — есть ли направленный сигнал от дневной модели (LONG или SHORT).
			bool hasDir = TryGetDirection (rec, out var goLongModel, out _);

			// isRiskDay — решение SL-модели по дню (рискованный / нерискованный).
			bool isRiskDay = rec.SlHighDecision;

			double leverage = policy.ResolveLeverage (rec);
			string policyName = policy.GetType ().Name;

			// --- BASE branch: торгует только нерискованные дни по направлению модели ---
				{
				bool skipped = !hasDir || isRiskDay;
				bool goLongBase = goLongModel;

				var row = BuildRow (
					policyName,
					branch: "BASE",
					rec: rec,
					isRiskDay: isRiskDay,
					hasDirection: hasDir,
					skipped: skipped,
					goLong: goLongBase,
					leverage: leverage,
					walletBalanceUsd: walletBalanceUsd);

				snapshot.PolicyRows.Add (row);
				}

			// --- ANTI-D branch: берёт только рискованные дни и переворачивает направление ---
				{
				bool skipped = !hasDir || !isRiskDay;
				bool goLongAntiD = goLongModel;

				if (!skipped && hasDir)
					{
					goLongAntiD = !goLongModel;
					}

				var row = BuildRow (
					policyName,
					branch: "ANTI-D",
					rec: rec,
					isRiskDay: isRiskDay,
					hasDirection: hasDir,
					skipped: skipped,
					goLong: goLongAntiD,
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
				Direction = (!hasDirection || skipped) ? "-" : (goLong ? "LONG" : "SHORT"),
				Leverage = leverage,
				Entry = rec.Entry
				};

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
					_ => r.PredLabel.ToString (CultureInfo.InvariantCulture)
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
