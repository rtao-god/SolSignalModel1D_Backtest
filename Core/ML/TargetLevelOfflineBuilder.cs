using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Trading;

namespace SolSignalModel1D_Backtest.Core.ML
	{
	/// <summary>
	/// Строит оффлайновый датасет для таргет-слоя (A/B) симметрично:
	/// на каждый день делаем два сценария — гипотетический long и гипотетический short.
	/// Это убирает зависимость от фактического дневного label и делает датасет достаточно толстым.
	/// </summary>
	public static class TargetLevelOfflineBuilder
		{
		// те же параметры, что и в DayExecutor
		private const double DeepDelayFactor = 0.35;
		private const double DeepMaxDelayHours = 4.0;

		private const double ShallowDelayFactor = 0.15;
		private const double ShallowMaxDelayHours = 2.0;

		public static List<TargetLevelSample> Build (
			List<DataRow> rows,
			IReadOnlyList<Candle1h> sol1h,
			Dictionary<DateTime, Candle6h> sol6hDict )
			{
			var result = new List<TargetLevelSample> (rows.Count * 2);

			foreach (var r in rows)
				{
				// нужна 6h-свеча на момент дня
				if (!sol6hDict.TryGetValue (r.Date, out var dayCandle))
					continue;

				double entryPrice = dayCandle.Close;
				double dayMinMove = r.MinMove;
				if (dayMinMove <= 0) dayMinMove = 0.02;

				// 24 часа 1h
				DateTime dayEnd = r.Date.AddHours (24);
				var dayHours = sol1h
					.Where (h => h.OpenTimeUtc >= r.Date && h.OpenTimeUtc < dayEnd)
					.OrderBy (h => h.OpenTimeUtc)
					.ToList ();
				if (dayHours.Count == 0)
					continue;

				// делаем два кейса: лонг и шорт
				BuildForDir (result, r, dayHours, entryPrice, dayMinMove, goLong: true);
				BuildForDir (result, r, dayHours, entryPrice, dayMinMove, goLong: false);
				}

			return result;
			}

		private static void BuildForDir (
			List<TargetLevelSample> sink,
			DataRow r,
			List<Candle1h> dayHours,
			double entryPrice,
			double dayMinMove,
			bool goLong )
			{
			bool strongSignal = true; // оффлайн: считаем, что если уж строим отложку, то день "сигнальный"

			// 1) базовый результат
			var baseOutcome = HourlyTradeEvaluator.EvaluateOne (
				dayHours,
				r.Date,
				goLong,
				!goLong,
				entryPrice,
				dayMinMove,
				strongSignal
			);

			// 2) deep delayed (A)
			var deepDelayed = DelayedEntryEvaluator.Evaluate (
				dayHours,
				r.Date,
				goLong,
				!goLong,
				entryPrice,
				dayMinMove,
				strongSignal,
				DeepDelayFactor,
				DeepMaxDelayHours
			);

			// 3) shallow delayed (B)
			var shDelayed = DelayedEntryEvaluator.Evaluate (
				dayHours,
				r.Date,
				goLong,
				!goLong,
				entryPrice,
				dayMinMove,
				strongSignal,
				ShallowDelayFactor,
				ShallowMaxDelayHours
			);

			// === решение по лейблу ===
			int label = 0;

			// сначала проверяем deep
			if (deepDelayed.Executed)
				{
				if (IsDeepImprovement (baseOutcome, deepDelayed))
					{
					label = 2;
					}
				}

			// если deep не дал улучшения — пробуем shallow
			if (label == 0 && shDelayed.Executed)
				{
				if (IsShallowNotWorse (baseOutcome, shDelayed))
					{
					label = 1;
					}
				}

			// фичи — такие же, как в онлайне
			var feats = TargetLevelFeatureBuilder.Build (
				r.Date,
				goLong,
				strongSignal,
				dayMinMove,
				entryPrice,
				dayHours
			);

			sink.Add (new TargetLevelSample
				{
				Label = label,
				Features = feats,
				EntryUtc = r.Date
				});
			}

		/// <summary>
		/// Для A: улучшением считаем случаи "deep исполнился и сделал не хуже, а чаще — лучше, чем базовый вход".
		/// </summary>
		private static bool IsDeepImprovement ( HourlyTradeOutcome baseOutcome, DelayedEntryResult delayed )
			{
			// 1) базовый был SL/None, delayed дал TP → однозначное улучшение
			if ((baseOutcome.Result == HourlyTradeResult.SlFirst || baseOutcome.Result == HourlyTradeResult.None) &&
				delayed.Result == DelayedIntradayResult.TpFirst)
				{
				return true;
				}

			// 2) и там, и там TP → тоже ок, deep не хуже
			if (baseOutcome.Result == HourlyTradeResult.TpFirst &&
				delayed.Result == DelayedIntradayResult.TpFirst)
				{
				return true;
				}

			// 3) оба SL, но delayed SL меньше → тоже можно считать улучшением
			if (baseOutcome.Result == HourlyTradeResult.SlFirst &&
				delayed.Result == DelayedIntradayResult.SlFirst &&
				delayed.SlPct > 0 && baseOutcome.SlPct > 0 &&
				delayed.SlPct < baseOutcome.SlPct)
				{
				return true;
				}

			return false;
			}

		/// <summary>
		/// Для B: допускаем "не хуже".
		/// </summary>
		private static bool IsShallowNotWorse ( HourlyTradeOutcome baseOutcome, DelayedEntryResult delayed )
			{
			int baseRank = RankHourly (baseOutcome.Result);
			int delayedRank = RankDelayed (delayed.Result);

			// delayed должен быть хотя бы не хуже
			if (delayedRank < baseRank)
				return false;

			// если оба SL, как и для A, можно посмотреть на размер
			if (baseOutcome.Result == HourlyTradeResult.SlFirst &&
				delayed.Result == DelayedIntradayResult.SlFirst &&
				delayed.SlPct > 0 && baseOutcome.SlPct > 0 &&
				delayed.SlPct < baseOutcome.SlPct)
				{
				return true;
				}

			// если delayed None, а base SL — уже не хуже
			if (delayed.Result == DelayedIntradayResult.None &&
				baseOutcome.Result == HourlyTradeResult.SlFirst)
				{
				return true;
				}

			return delayedRank >= baseRank;
			}

		private static int RankHourly ( HourlyTradeResult res )
			{
			return res switch
				{
					HourlyTradeResult.TpFirst => 3,
					HourlyTradeResult.None => 2,
					HourlyTradeResult.Ambiguous => 2,
					HourlyTradeResult.SlFirst => 0,
					_ => 0
					};
			}

		private static int RankDelayed ( DelayedIntradayResult res )
			{
			return res switch
				{
					DelayedIntradayResult.TpFirst => 3,
					DelayedIntradayResult.None => 2,
					DelayedIntradayResult.Ambiguous => 2,
					DelayedIntradayResult.SlFirst => 0,
					_ => 0
					};
			}
		}
	}
