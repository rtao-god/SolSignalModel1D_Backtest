using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using SolSignalModel1D_Backtest.Core.Analytics.Labeling;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Causal.Data;

namespace SolSignalModel1D_Backtest.Tests.Analytics.Labeling
	{
	/// <summary>
	/// Тест горизонта PathLabeler:
	/// label и все path-метрики не должны зависеть от минуток
	/// ПОСЛЕ baseline-exit (Windowing.ComputeBaselineExitUtc).
	/// </summary>
	public sealed class PathLabelerHorizonTests
		{
		[Fact]
		public void Label_DoesNotChange_WhenMinutesAfterExitAreMutated ()
			{
			// Берём будний день, чтобы Windowing.ComputeBaselineExitUtc не ругался.
			var entryUtc = new DateTime (2020, 2, 24, 15, 0, 0, DateTimeKind.Utc); // понедельник
			var exitUtc = Windowing.ComputeBaselineExitUtc (entryUtc);

			// entryPrice и minMove как в реальной логике.
			double entryPrice = 100.0;
			double minMove = 0.02;

			// Генерируем 1m-серию: от entryUtc - 1h до exitUtc + 12h,
			// чтобы наверняка покрыть весь baseline-горизонт и "дальнее будущее".
			var minutes = new List<Candle1m> ();

			var start = entryUtc.AddHours (-1);
			var end = exitUtc.AddHours (12);
			int totalMinutes = (int) (end - start).TotalMinutes;

			for (int i = 0; i <= totalMinutes; i++)
				{
				var t = start.AddMinutes (i);
				double price = entryPrice * (1.0 + 0.0001 * i);

				minutes.Add (new Candle1m
					{
					OpenTimeUtc = t,
					Close = price,
					High = price + 0.0005,
					Low = price - 0.0005
					});
				}

			// A-сценарий: исходные минутки.
			int dirA;
			DateTime? timeA;
			double upA, downA;

			int labelA = PathLabeler.AssignLabel (
				entryUtc: entryUtc,
				entryPrice: entryPrice,
				minMove: minMove,
				minutes: minutes,
				firstPassDir: out dirA,
				firstPassTimeUtc: out timeA,
				reachedUpPct: out upA,
				reachedDownPct: out downA);

			// B-сценарий: мутируем ВСЕ минутки ПОСЛЕ exitUtc, чтобы
			// они выглядели как "адский ракета в космос".
			var minutesB = minutes
				.Select (m => new Candle1m
					{
					OpenTimeUtc = m.OpenTimeUtc,
					Close = m.Close,
					High = m.High,
					Low = m.Low
					})
				.ToList ();

			foreach (var m in minutesB)
				{
				if (m.OpenTimeUtc >= exitUtc)
					{
					m.Close *= 10.0;
					m.High = m.Close + 0.0005;
					m.Low = m.Close - 0.0005;
					}
				}

			int dirB;
			DateTime? timeB;
			double upB, downB;

			int labelB = PathLabeler.AssignLabel (
				entryUtc: entryUtc,
				entryPrice: entryPrice,
				minMove: minMove,
				minutes: minutesB,
				firstPassDir: out dirB,
				firstPassTimeUtc: out timeB,
				reachedUpPct: out upB,
				reachedDownPct: out downB);

			// Проверяем, что всё, что PathLabeler возвращает, совпадает,
			// несмотря на то, что "будущее после exit" мы полностью сломали.

			Assert.Equal (labelA, labelB);
			Assert.Equal (dirA, dirB);
			Assert.Equal (timeA, timeB);
			Assert.Equal (upA, upB, 10);
			Assert.Equal (downA, downB, 10);
			}
		}
	}
