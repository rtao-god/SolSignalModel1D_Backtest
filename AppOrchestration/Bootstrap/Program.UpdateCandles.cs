using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using SolSignalModel1D_Backtest.Core.Data.Candles;
using SolSignalModel1D_Backtest.Core.Domain;
using SolSignalModel1D_Backtest.Core.Infra;

namespace SolSignalModel1D_Backtest
	{
	public partial class Program
		{
		/// <summary>
		/// Стартовая дата для полной истории свечей.
		/// Всё, что раньше, модели не нужно и не гарантируется.
		/// </summary>
		private static readonly DateTime FullBackfillFromUtc =
			new DateTime (2021, 8, 2, 0, 0, 0, DateTimeKind.Utc);

		/// <summary>
		/// Проверяет один TF-файл по символу:
		/// - если файла нет — ничего не делает;
		/// - если файл есть, но первая свеча позже FullBackfillFromUtc — файл удаляется как битый/неполный.
		/// Любые ошибки чтения/парсинга пробрасываются наверх.
		/// </summary>
		private static void EnsureTfHistoryOrDelete ( string symbol, string tfSuffix )
			{
			var path = CandlePaths.File (symbol, tfSuffix);
			if (!File.Exists (path))
				{
				Console.WriteLine ($"[update] {symbol}-{tfSuffix}: file not found at {path}");
				return;
				}

			Console.WriteLine ($"[update] {symbol}-{tfSuffix}: checking file {path}");

			var store = new CandleNdjsonStore (path);
			var first = store.TryGetFirstTimestampUtc ();

			Console.WriteLine ($"[update] {symbol}-{tfSuffix}: first={first?.ToString ("yyyy-MM-dd HH:mm") ?? "null"} (FullBackfillFromUtc={FullBackfillFromUtc:yyyy-MM-dd})");

			if (!first.HasValue || first.Value > FullBackfillFromUtc)
				{
				Console.WriteLine ($"[update] {symbol}-{tfSuffix}: deleting file as bad/incomplete");
				File.Delete (path);
				}
			}

		/// <summary>
		/// Проверяет набор TF по символу (1m/1h/6h) и удаляет неполные/битые файлы.
		/// </summary>     
		private static bool NeedsFullBackfill ( string symbol )
			{
			string[] tfs = { "1m", "1h", "6h" };
			bool needsFull = false;

			foreach (var tf in tfs)
				{
				var path = CandlePaths.File (symbol, tf);
				bool exists = File.Exists (path);
				Console.WriteLine ($"[update] NeedsFullBackfill check: {symbol}-{tf} exists={exists} path={path}");

				if (!exists)
					{
					needsFull = true;
					}
				}

			return needsFull;
			}

		private static void EnsureSymbolHistoryOrDeleteBad ( string symbol )
			{
			EnsureTfHistoryOrDelete (symbol, "1m");
			EnsureTfHistoryOrDelete (symbol, "1h");
			EnsureTfHistoryOrDelete (symbol, "6h");
			}

		/// <summary>
		/// После чистки проверяет, нужен ли этому символу полный бэкофилл.
		/// Критерий: если нет хотя бы одного TF-файла (1m/1h/6h) — нужен полный бэкофилл.
		/// </summary>

		/// <summary>
		/// Обновляет свечи SOL/USDT, BTC/USDT и PAXG/USDT.
		/// Контракт:
		/// - если по символу нет полной истории (любого TF) → выполняется полный бэкофилл с FullBackfillFromUtc;
		/// - иначе догоняются только хвосты по всем TF.
		/// Любые ошибки из CandleDailyUpdater пробрасываются наверх.
		/// </summary>
		private static async Task UpdateCandlesAsync ( HttpClient http )
			{
			Console.WriteLine ("[update] Updating SOL/USDT, BTC/USDT, PAXG/USDT candles...");

			Console.WriteLine ($"[update] CacheRoot = {PathConfig.CacheRoot}");
			Console.WriteLine ($"[update] CandlesDir = {PathConfig.CandlesDir}");

			var solSymbol = TradingSymbols.SolUsdtInternal;
			var btcSymbol = TradingSymbols.BtcUsdtInternal;
			var paxgSymbol = TradingSymbols.PaxgUsdtInternal;

			Console.WriteLine ($"[update] solSymbol = {solSymbol}, btcSymbol = {btcSymbol}, paxgSymbol = {paxgSymbol}");

			// 1. Чистим некорректные/укороченные файлы по каждому символу.
			EnsureSymbolHistoryOrDeleteBad (solSymbol);
			EnsureSymbolHistoryOrDeleteBad (btcSymbol);
			EnsureSymbolHistoryOrDeleteBad (paxgSymbol);

			// 2. Определяем, нужен ли полный бэкофилл для символа (нет хотя бы одного TF-файла).
			bool solNeedsFull = NeedsFullBackfill (solSymbol);
			bool btcNeedsFull = NeedsFullBackfill (btcSymbol);
			bool paxgNeedsFull = NeedsFullBackfill (paxgSymbol);

			Console.WriteLine ($"[update] NeedsFullBackfill: SOL={solNeedsFull}, BTC={btcNeedsFull}, PAXG={paxgNeedsFull}");

			var solUpdater = new CandleDailyUpdater (
				http,
				solSymbol,
				PathConfig.CandlesDir,
				catchupDays: 3
			);

			var btcUpdater = new CandleDailyUpdater (
				http,
				btcSymbol,
				PathConfig.CandlesDir,
				catchupDays: 3
			);

			var paxgUpdater = new CandleDailyUpdater (
				http,
				paxgSymbol,
				PathConfig.CandlesDir,
				catchupDays: 3
			);

			await Task.WhenAll
			(
				solUpdater.UpdateAllAsync (solNeedsFull ? FullBackfillFromUtc : (DateTime?) null),
				btcUpdater.UpdateAllAsync (btcNeedsFull ? FullBackfillFromUtc : (DateTime?) null),
				paxgUpdater.UpdateAllAsync (paxgNeedsFull ? FullBackfillFromUtc : (DateTime?) null)
			);

			Console.WriteLine ("[update] Candle update done.");
			}

		}
	}
