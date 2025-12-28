using System;
using System.Collections.Generic;
using Xunit;
using SolSignalModel1D_Backtest.Core.Causal.Analytics.MinMove;
using SolSignalModel1D_Backtest.Core.Omniscient.Utils.Time;
using SolSignalModel1D_Backtest.Core.Causal.Utils.Time;

namespace SolSignalModel1D_Backtest.Tests.Analytics.MinMove
	{
	public sealed class MinMoveNoLookaheadTests
		{
		[Fact]
		public void ComputeAdaptive_DoesNotDependOnFuturePathHistory ()
			{
			// История по day-key датам (00:00 UTC). В тесте важно:
			// - asOfUtc лежит внутри суток (проверяем нормализацию к day-key),
			// - "будущее" относительно asOfDay должно не влиять на результат.
			var historyBase = new List<MinMoveHistoryRow> ();
			var firstDay = new DateTime (2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
			const int totalDays = 400;

			for (int i = 0; i < totalDays; i++)
				{
				var dayUtc = firstDay.AddDays (i).ToCausalDateUtc ();

				// Монотонный рост амплитуды даёт нетривиальные квантили/ewma.
				double amp = 0.01 + 0.0001 * i;

				historyBase.Add (new MinMoveHistoryRow (
					DateUtc: dayUtc,
					RealizedPathAmpPct: amp));
				}

			// Конфиг фиксируем явно, чтобы будущие правки дефолтов не меняли смысл теста "тихо".
			var cfg = new MinMoveConfig
				{
				MinFloorPct = 0.015,
				MinCeilPct = 0.08,
				AtrWeight = 0.6,
				DynVolWeight = 0.4,
				EwmaAlpha = 0.15,
				QuantileStart = 0.6,
				QuantileLow = 0.5,
				QuantileHigh = 0.8,
				QuantileWindowDays = 90,
				QuantileRetuneEveryDays = 10,
				RegimeDownMul = 1.2
				};

			var asOfUtc = firstDay.AddDays (200).AddHours (12);
			var asOfDay = asOfUtc.ToCausalDateUtc ();

			// Раздельные состояния нужны, чтобы поймать зависимость не только результата,
			// но и внутренней адаптации (ewma/квантиль/дата ретюна).
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

			var resultA = MinMoveEngine.ComputeAdaptive (
				asOfUtc: asOfUtc,
				regimeDown: false,
				atrPct: 0.02,
				dynVol: 0.015,
				historyRows: historyBase,
				cfg: cfg,
				state: stateA);

			// Мутируем строго будущую часть истории (day-key > asOfDay).
			// Если код случайно читает "future", адаптация/результат должны поехать.
			var historyMutated = new List<MinMoveHistoryRow> (historyBase.Count);
			bool hasFutureMutation = false;

			foreach (var r in historyBase)
				{
				var amp = r.RealizedPathAmpPct;

				if (r.DateUtc > asOfDay)
					{
					amp = 0.5;
					hasFutureMutation = true;
					}

				historyMutated.Add (new MinMoveHistoryRow (
					DateUtc: r.DateUtc,
					RealizedPathAmpPct: amp));
				}

			Assert.True (hasFutureMutation);

			var resultB = MinMoveEngine.ComputeAdaptive (
				asOfUtc: asOfUtc,
				regimeDown: false,
				atrPct: 0.02,
				dynVol: 0.015,
				historyRows: historyMutated,
				cfg: cfg,
				state: stateB);

			// No-lookahead: идентичность по результату и по состоянию.
			Assert.Equal (resultA.MinMove, resultB.MinMove, 10);
			Assert.Equal (resultA.LocalVol, resultB.LocalVol, 10);
			Assert.Equal (resultA.EwmaVol, resultB.EwmaVol, 10);
			Assert.Equal (resultA.QuantileUsed, resultB.QuantileUsed, 10);

			Assert.Equal (stateA.EwmaVol, stateB.EwmaVol, 10);
			Assert.Equal (stateA.QuantileQ, stateB.QuantileQ, 10);
			Assert.Equal (stateA.LastQuantileTune.ToCausalDateUtc (), stateB.LastQuantileTune.ToCausalDateUtc ());

			// Контрольная проверка: когда asOf смещается вперёд, часть ранее "future" становится прошлым,
			// и тогда мутация обязана начать влиять (иначе тест выше может быть ложноположительным).
			var asOfUtcLater = firstDay.AddDays (240).AddHours (12);

			var stateBaseLater = new MinMoveState
				{
				EwmaVol = 0.0,
				QuantileQ = 0.0,
				LastQuantileTune = DateTime.MinValue
				};

			var stateMutLater = new MinMoveState
				{
				EwmaVol = 0.0,
				QuantileQ = 0.0,
				LastQuantileTune = DateTime.MinValue
				};

			var resultBaseLater = MinMoveEngine.ComputeAdaptive (
				asOfUtc: asOfUtcLater,
				regimeDown: false,
				atrPct: 0.06,
				dynVol: 0.06,
				historyRows: historyBase,
				cfg: cfg,
				state: stateBaseLater);

			var resultMutLater = MinMoveEngine.ComputeAdaptive (
				asOfUtc: asOfUtcLater,
				regimeDown: false,
				atrPct: 0.06,
				dynVol: 0.06,
				historyRows: historyMutated,
				cfg: cfg,
				state: stateMutLater);

			Assert.NotEqual (resultBaseLater.QuantileUsed, resultMutLater.QuantileUsed);
			Assert.NotEqual (resultBaseLater.MinMove, resultMutLater.MinMove);
			}

		[Fact]
		public void ComputeAdaptive_UsesStrictQuantileWindowDays ()
			{
			// Этот тест фиксирует границы окна:
			// window = [asOfDayKey-N .. asOfDayKey-1] ровно N дней.
			//
			// Вокруг границ специально кладём "лишние" дни:
			// - day = asOfDayKey-N-1 (лишний слева),
			// - day = asOfDayKey (сегодня, его нельзя включать).
			// Любая off-by-one ошибка изменит size окна (N±1), что детектится по result.Notes.
			const int N = 90;

			var firstDay = new DateTime (2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
			var asOfUtc = firstDay.AddDays (200).AddHours (12);
			var asOfDayKey = asOfUtc.ToCausalDateUtc ();

			var history = new List<MinMoveHistoryRow> ();

			// Диапазон: [asOfDayKey-(N+1) .. asOfDayKey], это N+2 day-key записей.
			// Правильное окно внутри этого диапазона должно выбрать ровно N записей:
			// [asOfDayKey-N .. asOfDayKey-1].
			for (int i = -(N + 1); i <= 0; i++)
				{
				var day = asOfDayKey.AddDays (i).ToCausalDateUtc ();

				// Значения амплитуды не должны быть NaN/Inf/<=0: тест здесь про границы окна.
				double amp = 0.02 + 0.00001 * (i + (N + 1));
				history.Add (new MinMoveHistoryRow (day, amp));
				}

			var cfg = new MinMoveConfig
				{
				MinFloorPct = 0.015,
				MinCeilPct = 0.08,
				AtrWeight = 0.6,
				DynVolWeight = 0.4,
				EwmaAlpha = 0.15,

				QuantileStart = 0.6,
				QuantileLow = 0.5,
				QuantileHigh = 0.8,

				QuantileWindowDays = N,
				QuantileRetuneEveryDays = 1,
				RegimeDownMul = 1.2
				};

			var state = new MinMoveState
				{
				EwmaVol = 0.0,
				QuantileQ = 0.0,
				LastQuantileTune = DateTime.MinValue
				};

			var result = MinMoveEngine.ComputeAdaptive (
				asOfUtc: asOfUtc,
				regimeDown: false,
				atrPct: 0.02,
				dynVol: 0.02,
				historyRows: history,
				cfg: cfg,
				state: state);

			// Гарантия, что ретюн реально выполнялся (иначе проверка window-size может оказаться тривиальной).
			Assert.Contains ("retune=1", result.Notes);

			int window = ExtractWindowCountOrThrow (result.Notes);
			Assert.Equal (N, window);

			Assert.Equal (asOfDayKey, state.LastQuantileTune.ToCausalDateUtc ());
			}

		private static int ExtractWindowCountOrThrow ( string notes )
			{
			if (notes == null) throw new ArgumentNullException (nameof (notes));

			const string key = "window=";
			int i = notes.IndexOf (key, StringComparison.Ordinal);
			if (i < 0)
				throw new InvalidOperationException ($"[test] 'window=' not found in Notes: '{notes}'.");

			i += key.Length;

			int j = i;
			while (j < notes.Length && char.IsDigit (notes[j]))
				j++;

			if (j == i)
				throw new InvalidOperationException ($"[test] window value not found in Notes: '{notes}'.");

			return int.Parse (notes.Substring (i, j - i));
			}
		}
	}
