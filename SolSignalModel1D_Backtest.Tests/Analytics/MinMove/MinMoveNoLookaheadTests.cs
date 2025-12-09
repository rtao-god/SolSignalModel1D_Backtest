using System;
using System.Collections.Generic;
using Xunit;
using SolSignalModel1D_Backtest.Core.Analytics.MinMove;
using SolSignalModel1D_Backtest.Core.Causal.Data;

namespace SolSignalModel1D_Backtest.Tests.Analytics.MinMove
	{
	/// <summary>
	/// Тест на отсутствие lookahead в MinMoveEngine:
	/// результат ComputeAdaptive(asOfUtc, ...) не должен зависеть от historyRows
	/// с датами строго > asOfUtc.Date.
	/// </summary>
	public sealed class MinMoveNoLookaheadTests
		{
		[Fact]
		public void ComputeAdaptive_DoesNotDependOnFuturePathHistory ()
			{
			// Строим длинную историю historyRows (400 дней),
			// так чтобы для asOfUtc была и приличная "прошлая" выборка, и "будущее".
			var historyBase = new List<DataRow> ();
			var firstDate = new DateTime (2020, 1, 1, 8, 0, 0, DateTimeKind.Utc);
			int totalDays = 400;

			for (int i = 0; i < totalDays; i++)
				{
				var dt = firstDate.AddDays (i);

				// Path-амплитуды: плавно растущие значения.
				double up = 0.01 + 0.0001 * i;
				double down = -0.008 - 0.00005 * i;

				var row = new DataRow
					{
					Date = dt,
					PathReachedUpPct = up,
					PathReachedDownPct = down,
					// Остальные поля DataRow здесь не важны для MinMoveEngine,
					// но при необходимости могут быть заполнены дефолтами.
					};

				historyBase.Add (row);
				}

			// asOfUtc — примерно середина истории.
			var asOfUtc = firstDate.AddDays (200).AddHours (12);

			// Конфиг и независимые состояния для двух сценариев.
			var cfg = new MinMoveConfig
				{
				// Оставляем дефолтные значения из класса, чтобы тест не зависел от конкретных цифр.
				};

			var stateA = new MinMoveState
				{
				EwmaVol = 0.0,
				QuantileQ = 0.0,
				LastQuantileTune = DateTime.MinValue
				};

			var stateB = new MinMoveState
				{
				EwmaVol = 0.0,
				QuantileQ = 0.0,
				LastQuantileTune = DateTime.MinValue
				};

			// Сценарий A: базовая история без мутаций.
			var resultA = MinMoveEngine.ComputeAdaptive (
				asOfUtc: asOfUtc,
				regimeDown: false,
				atrPct: 0.02,
				dynVol: 0.015,
				historyRows: historyBase,
				cfg: cfg,
				state: stateA);

			// Сценарий B: копия истории, но "будущее" после asOfUtc.Date
			// жёстко мутируется (амплитуды path становятся огромными).
			var historyMutated = new List<DataRow> (historyBase.Count);
			foreach (var r in historyBase)
				{
				var clone = new DataRow
					{
					Date = r.Date,
					PathReachedUpPct = r.PathReachedUpPct,
					PathReachedDownPct = r.PathReachedDownPct
					};

				if (clone.Date.Date > asOfUtc.Date)
					{
					// Сильно увеличиваем амплитуды, чтобы эффект точно был заметен,
					// если MinMoveEngine вдруг смотрит в будущее.
					clone.PathReachedUpPct = 0.5;
					clone.PathReachedDownPct = -0.5;
					}

				historyMutated.Add (clone);
				}

			var resultB = MinMoveEngine.ComputeAdaptive (
				asOfUtc: asOfUtc,
				regimeDown: false,
				atrPct: 0.02,
				dynVol: 0.015,
				historyRows: historyMutated,
				cfg: cfg,
				state: stateB);

			// Проверяем, что результаты полностью совпадают:
			// будущее (Date > asOf.Date) не должно влиять на MinMove/Vol/Q.
			Assert.Equal (resultA.MinMove, resultB.MinMove, 10);
			Assert.Equal (resultA.LocalVol, resultB.LocalVol, 10);
			Assert.Equal (resultA.EwmaVol, resultB.EwmaVol, 10);
			Assert.Equal (resultA.QuantileUsed, resultB.QuantileUsed, 10);

			// Заодно убеждаемся, что состояние тоже не зависит от future-части.
			Assert.Equal (stateA.EwmaVol, stateB.EwmaVol, 10);
			Assert.Equal (stateA.QuantileQ, stateB.QuantileQ, 10);
			Assert.Equal (stateA.LastQuantileTune.Date, stateB.LastQuantileTune.Date);
			}
		}
	}
