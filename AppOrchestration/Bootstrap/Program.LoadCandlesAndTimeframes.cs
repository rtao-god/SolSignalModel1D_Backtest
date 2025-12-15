using SolSignalModel1D_Backtest.Core.Data.Candles;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Domain;
using SolSignalModel1D_Backtest.Core.Utils;
using System.Diagnostics;

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
			var sw = Stopwatch.StartNew ();
			Console.WriteLine ("[perf] LoadAllCandlesAndWindow... start");

			var solSym = TradingSymbols.SolUsdtInternal;
			var btcSym = TradingSymbols.BtcUsdtInternal;
			var paxgSym = TradingSymbols.PaxgUsdtInternal;

			CandleResampler.Ensure6hAvailable (solSym);
			CandleResampler.Ensure6hAvailable (btcSym);
			CandleResampler.Ensure6hAvailable (paxgSym);

			solAll6h = ReadAll6h (solSym);
			btcAll6h = ReadAll6h (btcSym);
			paxgAll6h = ReadAll6h (paxgSym);

			if (solAll6h.Count == 0 || btcAll6h.Count == 0 || paxgAll6h.Count == 0)
				throw new InvalidOperationException ("[init] Пустые 6h серии: SOL/BTC/PAXG. Проверить cache/candles/*.ndjson");

			// ЕДИНСТВЕННАЯ нормализация порядка 6h: дальше везде только проверки.
			SeriesGuards.SortByKeyUtcInPlace (solAll6h, c => c.OpenTimeUtc, "SOL 6h");
			SeriesGuards.SortByKeyUtcInPlace (btcAll6h, c => c.OpenTimeUtc, "BTC 6h");
			SeriesGuards.SortByKeyUtcInPlace (paxgAll6h, c => c.OpenTimeUtc, "PAXG 6h");

			Console.WriteLine ($"[6h] SOL={solAll6h.Count}, BTC={btcAll6h.Count}, PAXG={paxgAll6h.Count}");

			solAll1h = ReadAll1h (solSym);
			if (solAll1h.Count == 0)
				throw new InvalidOperationException ($"[init] Нет 1h свечей {TradingSymbols.SolUsdtDisplay} в cache/candles.");

			SeriesGuards.SortByKeyUtcInPlace (solAll1h, c => c.OpenTimeUtc, "SOL 1h");

			Console.WriteLine ($"[1h] SOL count = {solAll1h.Count}");

			var sol1mWeekdays = ReadAll1m (solSym);
			var sol1mWeekends = ReadAll1mWeekends (solSym);

			EnsureSortedAndStrictUnique1m (sol1mWeekdays, tag: "weekdays");
			EnsureSortedAndStrictUnique1m (sol1mWeekends, tag: "weekends");

			sol1m = MergeSortedStrictUnique1m (sol1mWeekdays, sol1mWeekends);

			if (sol1m.Count == 0)
				throw new InvalidOperationException ($"[init] Нет 1m свечей {TradingSymbols.SolUsdtDisplay} в cache/candles.");

			// ЕДИНСТВЕННАЯ нормализация порядка 1m: дальше строго без OrderBy по 1m.
			sol1m = MergeSortedStrictUnique1m (sol1mWeekdays, sol1mWeekends);

			if (sol1m.Count == 0)
				throw new InvalidOperationException ($"[init] Нет 1m свечей {TradingSymbols.SolUsdtDisplay} в cache/candles.");

			// Окно бэктеста относительно последней 6h-свечи SOL (после сортировки это последний элемент).
			var lastUtc = solAll6h[solAll6h.Count - 1].OpenTimeUtc;
			fromUtc = lastUtc.ToCausalDateUtc().AddDays (-540);
			toUtc = lastUtc.ToCausalDateUtc();

			sw.Stop ();
			Console.WriteLine ($"[perf] LoadAllCandlesAndWindow done in {sw.Elapsed.TotalSeconds:F1}s");
			}

		/// <summary>
		/// Загрузчик 1m-свечей только из weekend-файла (SYMBOL-1m-weekends.ndjson).
		/// </summary>
		private static List<Candle1m> ReadAll1mWeekends ( string symbol )
			{
			var path = CandlePaths.WeekendFile (symbol, "1m");

			if (!File.Exists (path))
				return new List<Candle1m> ();

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

		/// <summary>
		/// Гарантирует, что список Candle1m отсортирован по OpenTimeUtc и строго уникален по времени.
		/// Если порядок нарушен — сортируем один раз и затем всё равно валидируем.
		/// Дубли (>=) считаются фатальной проблемой данных, чтобы не маскировать ошибки источника.
		/// </summary>
		private static void EnsureSortedAndStrictUnique1m ( List<Candle1m> xs, string tag )
			{
			if (xs == null) throw new ArgumentNullException (nameof (xs));
			if (tag == null) throw new ArgumentNullException (nameof (tag));
			if (xs.Count <= 1) return;

			bool sorted = true;

			for (int i = 1; i < xs.Count; i++)
				{
				if (xs[i].OpenTimeUtc < xs[i - 1].OpenTimeUtc)
					{
					sorted = false;
					break;
					}
				}

			if (!sorted)
				{
				xs.Sort (( a, b ) => a.OpenTimeUtc.CompareTo (b.OpenTimeUtc));
				}

			for (int i = 1; i < xs.Count; i++)
				{
				var prev = xs[i - 1].OpenTimeUtc;
				var cur = xs[i].OpenTimeUtc;

				if (cur <= prev)
					{
					// cur == prev -> дубль, cur < prev -> логическая невозможность после сортировки (коррупция данных).
					throw new InvalidOperationException (
						$"[init][1m] {tag}: non-strict time sequence at idx={i}, prev={prev:O}, cur={cur:O}. " +
						"Дубли/пересечения по OpenTimeUtc недопустимы: проверь источники NDJSON и weekend-файл.");
					}
				}
			}

		/// <summary>
		/// Сливает два списка Candle1m, которые уже отсортированы и строго уникальны по OpenTimeUtc.
		/// Любое совпадение OpenTimeUtc между списками считается фатальной ошибкой (это пересечение weekday/weekend).
		/// </summary>
		private static List<Candle1m> MergeSortedStrictUnique1m ( List<Candle1m> a, List<Candle1m> b )
			{
			if (a == null) throw new ArgumentNullException (nameof (a));
			if (b == null) throw new ArgumentNullException (nameof (b));

			var res = new List<Candle1m> (a.Count + b.Count);

			int i = 0, j = 0;

			// lastTime нужен как дополнительный инвариант: итог тоже должен быть строго возрастающим.
			bool hasLast = false;
			DateTime lastTime = default;

			while (i < a.Count || j < b.Count)
				{
				Candle1m next;

				if (j >= b.Count)
					{
					next = a[i++];
					}
				else if (i >= a.Count)
					{
					next = b[j++];
					}
				else
					{
					var ta = a[i].OpenTimeUtc;
					var tb = b[j].OpenTimeUtc;

					if (ta < tb)
						next = a[i++];
					else if (tb < ta)
						next = b[j++];
					else
						{
						// Пересечение weekday/weekend по одному и тому же времени — это именно та проблема, которую ловим.
						throw new InvalidOperationException (
							$"[init][1m] overlap between weekdays/weekends at OpenTimeUtc={ta:O}. " +
							"Удали пересечения: weekend-файл не должен содержать минуты, которые уже есть в основном 1m.");
						}
					}

				var t = next.OpenTimeUtc;

				if (hasLast && t <= lastTime)
					{
					throw new InvalidOperationException (
						$"[init][1m] merged: non-strict time sequence, last={lastTime:O}, cur={t:O}. " +
						"Это не должно происходить при корректных входных данных.");
					}

				res.Add (next);
				lastTime = t;
				hasLast = true;
				}

			return res;
			}
		}
	}
