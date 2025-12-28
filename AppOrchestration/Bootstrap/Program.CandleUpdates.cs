using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using SolSignalModel1D_Backtest.Core.Causal.Data.Candles;
using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Gaps;
using SolSignalModel1D_Backtest.Core.Causal.Domain;
using SolSignalModel1D_Backtest.Core.Causal.Infra;

namespace SolSignalModel1D_Backtest
	{
	public partial class Program
		{
		/// <summary>
		/// Специальный флаг для тестов:
		/// если true, UpdateCandlesAsync вообще не трогает сеть и файлы.
		/// В прод-запуске ДОЛЖЕН оставаться false.
		/// </summary>
		public static bool DebugSkipCandleUpdatesForTests { get; set; } = false;

		/// <summary>
		/// Стартовая дата для полной истории свечей.
		/// Всё, что раньше, модели не нужно и не гарантируется.
		/// </summary>
		private static readonly DateTime FullBackfillFromUtc =
			new DateTime (2021, 8, 2, 0, 0, 0, DateTimeKind.Utc);

		/// <summary>
		/// Флаги включённых TF для каждого символа.
		/// Важно: сюда завязаны и EnsureSymbolHistoryOrDeleteBad, и CandleDailyUpdater.
		/// </summary>
		private static readonly CandleUpdateTf SolCandleTfs =
			CandleUpdateTf.M1 | CandleUpdateTf.H1 | CandleUpdateTf.H6;

		private static readonly CandleUpdateTf BtcCandleTfs =
			CandleUpdateTf.M1 | CandleUpdateTf.H6;

		private static readonly CandleUpdateTf PaxgCandleTfs =
			CandleUpdateTf.H6;

		private static readonly bool EnableSolCandleUpdates = true;
		private static readonly bool EnableBtcCandleUpdates = true;
		private static readonly bool EnablePaxgCandleUpdates = true;

		/// <summary>
		/// Проверяет один TF-файл по символу:
		/// - если файла нет — молча выходим (причину "missing" покажет preflight);
		/// - если файл есть, но первая свеча позже FullBackfillFromUtc — файл удаляется как битый/неполный.
		/// Любые ошибки чтения/парсинга пробрасываются наверх.
		/// </summary>
		private static void EnsureTfHistoryOrDelete ( string symbol, string tfSuffix )
			{
			var path = CandlePaths.File (symbol, tfSuffix);
			if (!File.Exists (path))
				{
				return;
				}

			var store = new CandleNdjsonStore (path);
			var first = store.TryGetFirstTimestampUtc ();

			if (!first.HasValue || first.Value > FullBackfillFromUtc)
				{
				var firstStr = first.HasValue ? first.Value.ToString ("O") : "null";
				CandleFsAudit.Delete (
					path,
					reason: $"{symbol}-{tfSuffix} bad/incomplete first={firstStr} required<= {FullBackfillFromUtc:O}");
				}
			}

		private static void EnsureSymbolHistoryOrDeleteBad ( string symbol )
			{
			EnsureSymbolHistoryOrDeleteBad (symbol, CandleUpdateTf.All);
			}

		private static void EnsureSymbolHistoryOrDeleteBad ( string symbol, CandleUpdateTf tfs )
			{
			if ((tfs & CandleUpdateTf.M1) != 0)
				EnsureTfHistoryOrDelete (symbol, "1m");

			if ((tfs & CandleUpdateTf.H1) != 0)
				EnsureTfHistoryOrDelete (symbol, "1h");

			if ((tfs & CandleUpdateTf.H6) != 0)
				EnsureTfHistoryOrDelete (symbol, "6h");
			}

		private static string BuildSymbolFilesStat ( string symbol, CandleUpdateTf tfs )
			{
			static string Stat ( string path )
				{
				if (!File.Exists (path))
					return $"{Path.GetFileName (path)}=missing";

				var fi = new FileInfo (path);
				double mb = fi.Length / 1024.0 / 1024.0;
				return $"{Path.GetFileName (path)}={mb:F1}MB";
				}

			var parts = new List<string> (capacity: 6);

			if ((tfs & CandleUpdateTf.M1) != 0)
				{
				parts.Add (Stat (CandlePaths.File (symbol, "1m")));
				parts.Add (Stat (CandlePaths.WeekendFile (symbol, "1m")));
				}

			if ((tfs & CandleUpdateTf.H1) != 0)
				parts.Add (Stat (CandlePaths.File (symbol, "1h")));

			if ((tfs & CandleUpdateTf.H6) != 0)
				parts.Add (Stat (CandlePaths.File (symbol, "6h")));

			return string.Join (", ", parts);
			}

		/// <summary>
		/// Обновляет свечи SOL/USDT, BTC/USDT и PAXG/USDT.
		/// Контракт:
		/// - если по символу нет полной истории (любой из включённых TF) → полный бэкофилл с FullBackfillFromUtc;
		/// - иначе догоняются только хвосты по включённым TF.
		/// Любые ошибки из CandleDailyUpdater пробрасываются наверх.
		/// </summary>
		private static async Task UpdateCandlesAsync ( HttpClient http )
			{
			if (DebugSkipCandleUpdatesForTests)
				{
				Console.WriteLine (
					$"[update] DebugSkipCandleUpdatesForTests=true, skipping. fullFrom={FullBackfillFromUtc:O}");
				return;
				}

			// Один короткий tag на запуск: помогает склеивать строки логов одного старта.
			CandleFsAudit.RunTag = Guid.NewGuid ().ToString ("N").Substring (0, 8);

			var solSymbol = TradingSymbols.SolUsdtInternal;
			var btcSymbol = TradingSymbols.BtcUsdtInternal;
			var paxgSymbol = TradingSymbols.PaxgUsdtInternal;

			var pfAsm = typeof (CandleUpdatePreflight).Assembly.Location;

			Console.WriteLine (
				$"[update] symbols SOL={solSymbol}, BTC={btcSymbol}, PAXG={paxgSymbol}, fullFrom={FullBackfillFromUtc:O}");

			// 1) Чистим некорректные/укороченные файлы по каждому символу
			// только по тем TF, которые реально включены.
			if (EnableSolCandleUpdates)
				EnsureSymbolHistoryOrDeleteBad (solSymbol, SolCandleTfs);
			if (EnableBtcCandleUpdates)
				EnsureSymbolHistoryOrDeleteBad (btcSymbol, BtcCandleTfs);
			if (EnablePaxgCandleUpdates)
				EnsureSymbolHistoryOrDeleteBad (paxgSymbol, PaxgCandleTfs);

			// 2) Preflight: компактно объясняем, почему будет FULL (или почему tail).
			CandleUpdatePreflight.Result? solPre = null;
			CandleUpdatePreflight.Result? btcPre = null;
			CandleUpdatePreflight.Result? paxgPre = null;

			if (EnableSolCandleUpdates)
				{
				solPre = CandleUpdatePreflight.Evaluate (solSymbol, SolCandleTfs, FullBackfillFromUtc, PathConfig.CandlesDir);
				Console.WriteLine (solPre.ToCompactLogLine (FullBackfillFromUtc));
				}
			else
				{
				Console.WriteLine ($"[update-check] {solSymbol}: updates disabled");
				}

			if (EnableBtcCandleUpdates)
				{
				btcPre = CandleUpdatePreflight.Evaluate (btcSymbol, BtcCandleTfs, FullBackfillFromUtc, PathConfig.CandlesDir);
				Console.WriteLine (btcPre.ToCompactLogLine (FullBackfillFromUtc));
				}
			else
				{
				Console.WriteLine ($"[update-check] {btcSymbol}: updates disabled");
				}

			if (EnablePaxgCandleUpdates)
				{
				paxgPre = CandleUpdatePreflight.Evaluate (paxgSymbol, PaxgCandleTfs, FullBackfillFromUtc, PathConfig.CandlesDir);
				Console.WriteLine (paxgPre.ToCompactLogLine (FullBackfillFromUtc));
				}
			else
				{
				Console.WriteLine ($"[update-check] {paxgSymbol}: updates disabled");
				}

			// 3) Режимы апдейта (full/tail) берём из preflight.
			bool solNeedsFull = EnableSolCandleUpdates && solPre != null && solPre.NeedsFullBackfill;
			bool btcNeedsFull = EnableBtcCandleUpdates && btcPre != null && btcPre.NeedsFullBackfill;
			bool paxgNeedsFull = EnablePaxgCandleUpdates && paxgPre != null && paxgPre.NeedsFullBackfill;

			// 4) Создаём апдейтеры с нужным профилем TF.
			var tasks = new List<Task> ();

			if (EnableSolCandleUpdates)
				{
				var solUpdater = new CandleDailyUpdater (
					http,
					solSymbol,
					PathConfig.CandlesDir,
					catchupDays: 3,
					enabledTf: SolCandleTfs
				);

				DateTime? solFrom = solNeedsFull ? FullBackfillFromUtc : (DateTime?) null;
				Console.WriteLine (
					$"[update] SOL UpdateAllAsync: mode={(solNeedsFull ? "full" : "tail")}, from={(solFrom.HasValue ? solFrom.Value.ToString ("O") : "auto")}");

				tasks.Add (solUpdater.UpdateAllAsync (solFrom));
				}
			else
				{
				Console.WriteLine ("[update] SOL UpdateAllAsync: disabled");
				}

			if (EnableBtcCandleUpdates)
				{
				var btcUpdater = new CandleDailyUpdater (
					http,
					btcSymbol,
					PathConfig.CandlesDir,
					catchupDays: 3,
					enabledTf: BtcCandleTfs
				);

				DateTime? btcFrom = btcNeedsFull ? FullBackfillFromUtc : (DateTime?) null;
				Console.WriteLine (
					$"[update] BTC UpdateAllAsync: mode={(btcNeedsFull ? "full" : "tail")}, from={(btcFrom.HasValue ? btcFrom.Value.ToString ("O") : "auto")}");

				tasks.Add (btcUpdater.UpdateAllAsync (btcFrom));
				}
			else
				{
				Console.WriteLine ("[update] BTC UpdateAllAsync: disabled");
				}

			if (EnablePaxgCandleUpdates)
				{
				var paxgUpdater = new CandleDailyUpdater (
					http,
					paxgSymbol,
					PathConfig.CandlesDir,
					catchupDays: 3,
					enabledTf: PaxgCandleTfs
				);

				DateTime? paxgFrom = paxgNeedsFull ? FullBackfillFromUtc : (DateTime?) null;
				Console.WriteLine (
					$"[update] PAXG UpdateAllAsync: mode={(paxgNeedsFull ? "full" : "tail")}, from={(paxgFrom.HasValue ? paxgFrom.Value.ToString ("O") : "auto")}");

				tasks.Add (paxgUpdater.UpdateAllAsync (paxgFrom));
				}
			else
				{
				Console.WriteLine ("[update] PAXG UpdateAllAsync: disabled");
				}

			if (tasks.Count > 0)
				await Task.WhenAll (tasks);

			// 5) Итоговый “факт” по файлам: по 1 строке на символ.
			if (EnableSolCandleUpdates)
				Console.WriteLine ($"[update-files] {solSymbol}: {BuildSymbolFilesStat (solSymbol, SolCandleTfs)}");
			if (EnableBtcCandleUpdates)
				Console.WriteLine ($"[update-files] {btcSymbol}: {BuildSymbolFilesStat (btcSymbol, BtcCandleTfs)}");
			if (EnablePaxgCandleUpdates)
				Console.WriteLine ($"[update-files] {paxgSymbol}: {BuildSymbolFilesStat (paxgSymbol, PaxgCandleTfs)}");
			}
		}
	}
