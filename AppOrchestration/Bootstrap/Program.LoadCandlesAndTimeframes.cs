using SolSignalModel1D_Backtest.Core.Data.Candles;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Domain;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace SolSignalModel1D_Backtest
	{
	/// <summary>
	/// Частичный класс Program: загрузка всех таймфреймов
	/// и вычисление окна бэктеста (fromUtc/toUtc).
	/// </summary>
	public partial class Program
		{
		/// <summary>
		/// Обеспечивает наличие 6h-рядов, загружает:
		/// - SOL/BTC/PAXG 6h;
		/// - SOL 1h;
		/// - SOL 1m (будни + выходные для PnL);
		/// + вычисляет fromUtc/toUtc по последней 6h-свечке SOL.
		/// </summary>
		private static void LoadAllCandlesAndWindow (
			out List<Candle6h> solAll6h,
			out List<Candle6h> btcAll6h,
			out List<Candle6h> paxgAll6h,
			out List<Candle1h> solAll1h,
			out List<Candle1m> sol1m,
			out DateTime fromUtc,
			out DateTime toUtc
		)
			{
			// Логируем время работы блока, чтобы видеть, тормозит ли он сам по себе.
			var sw = Stopwatch.StartNew ();
			Console.WriteLine ("[perf] LoadAllCandlesAndWindow... start");

			var solSym = TradingSymbols.SolUsdtInternal;
			var btcSym = TradingSymbols.BtcUsdtInternal;
			var paxgSym = TradingSymbols.PaxgUsdtInternal;

			// Гарантируем, что 6h-агрегации построены.
			CandleResampler.Ensure6hAvailable (solSym);
			CandleResampler.Ensure6hAvailable (btcSym);
			CandleResampler.Ensure6hAvailable (paxgSym);

			// Загружаем серии 6h.
			solAll6h = ReadAll6h (solSym);
			btcAll6h = ReadAll6h (btcSym);
			paxgAll6h = ReadAll6h (paxgSym);

			if (solAll6h.Count == 0 || btcAll6h.Count == 0 || paxgAll6h.Count == 0)
				throw new InvalidOperationException ("[init] Пустые 6h серии: SOL/BTC/PAXG. Проверь cache/candles/*.ndjson");

			Console.WriteLine ($"[6h] SOL={solAll6h.Count}, BTC={btcAll6h.Count}, PAXG={paxgAll6h.Count}");

			// Загружаем SOL 1h для SL/DelayedA-логики.
			solAll1h = ReadAll1h (solSym);
			Console.WriteLine ($"[1h] SOL count = {solAll1h.Count}");

			// Загружаем SOL 1m — отдельно будни и выходные.
			var sol1mWeekdays = ReadAll1m (solSym);
			var sol1mWeekends = ReadAll1mWeekends (solSym);

			// Объединяем в единый ряд для всех мест, где PnL и модель работают по 1m:
			// - PnL-движок;
			// - delayed-слой;
			// - любые path-based метрики на 1m.
			//
			// Если weekend-файл пока пустой, будет просто набор будних минут,
			sol1m = sol1mWeekdays
				.Concat (sol1mWeekends)
				.OrderBy (c => c.OpenTimeUtc)
				.ToList ();

			if (sol1m.Count == 0)
				throw new InvalidOperationException ($"[init] Нет 1m свечей {TradingSymbols.SolUsdtDisplay} в cache/candles.");

			// Вычисляем окно бэктеста относительно последней 6h-свечи по SOL.
			var lastUtc = solAll6h.Max (c => c.OpenTimeUtc);
			fromUtc = lastUtc.Date.AddDays (-540);
			toUtc = lastUtc.Date;

			sw.Stop ();
			Console.WriteLine ($"[perf] LoadAllCandlesAndWindow done in {sw.Elapsed.TotalSeconds:F1}s");
			}

		/// <summary>
		/// Загрузчик 1m-свечей только из weekend-файла (SYMBOL-1m-weekends.ndjson).
		/// Будни не подмешиваются — используются только в основном ReadAll1m().
		/// </summary>
		private static List<Candle1m> ReadAll1mWeekends ( string symbol )
			{
			var path = CandlePaths.WeekendFile (symbol, "1m");

			if (!File.Exists (path))
				{
				return new List<Candle1m> ();
				}

			var store = new CandleNdjsonStore (path);
			var lines = store.ReadRange (DateTime.MinValue, DateTime.MaxValue);

			var res = new List<Candle1m> (lines.Count);

			foreach (var line in lines)
				{
				res.Add (new Candle1m
					{
					OpenTimeUtc = line.OpenTimeUtc,
					Open = line.Open,
					High = line.High,
					Low = line.Low,
					Close = line.Close,
					});
				}

			return res;
			}
		}
	}
