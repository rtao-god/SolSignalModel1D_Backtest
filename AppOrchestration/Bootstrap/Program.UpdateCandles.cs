using SolSignalModel1D_Backtest.Core.Data.Candles;
using SolSignalModel1D_Backtest.Core.Domain;
using SolSignalModel1D_Backtest.Core.Infra;

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
		/// Важно: сюда завязаны и NeedsFullBackfill, и CandleDailyUpdater.
		/// </summary>
		private static readonly CandleUpdateTf SolCandleTfs =
			CandleUpdateTf.M1 | CandleUpdateTf.H1 | CandleUpdateTf.H6;

		/// <summary>
		/// BTC:
		/// - M1: для ликвидаций/точных фитилей (если/когда понадобятся);
		/// - H6: дневной слой.
		/// 1h можем не тянуть, пока не нужен.
		/// </summary>
		private static readonly CandleUpdateTf BtcCandleTfs =
			CandleUpdateTf.M1 | CandleUpdateTf.H6;

		/// <summary>
		/// PAXG:
		/// сейчас используется только 6h в дневной модели.
		/// Минутки и часы отключены, чтобы не плодить лишние файлы.
		/// </summary>
		private static readonly CandleUpdateTf PaxgCandleTfs =
			CandleUpdateTf.H6;

		/// <summary>
		/// Переключатели по символам:
		/// позволяют полностью отключить обновление свечей конкретного символа.
		/// Если отключить символ, но оставить его в CandleRequirementRegistry,
		/// пайплайн упадёт на этапе чтения — это ожидаемое поведение.
		/// </summary>
		private static readonly bool EnableSolCandleUpdates = true;
		private static readonly bool EnableBtcCandleUpdates = true;
		private static readonly bool EnablePaxgCandleUpdates = true;

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

			var store = new CandleNdjsonStore (path);
			var first = store.TryGetFirstTimestampUtc ();

			if (!first.HasValue || first.Value > FullBackfillFromUtc)
				{
				Console.WriteLine ($"[update] {symbol}-{tfSuffix}: deleting file as bad/incomplete");
				File.Delete (path);
				}
			}

		/// <summary>
		/// Обёртка над EnsureTfHistoryOrDelete с учётом включённых TF.
		/// Старый вариант без TF-флагов оставлен для обратной совместимости.
		/// </summary>
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

		/// <summary>
		/// Определяет, нужен ли полный бэкофилл по символу с учётом включённых TF.
		/// Логика:
		/// - если по включённому TF нет файла => полный бэкофилл;
		/// - для 1m дополнительно:
		///   - нужен weekend-файл SYMBOL-1m-weekends.ndjson;
		///   - первая свеча в weekend-файле должна быть ≤ FullBackfillFromUtc,
		///     иначе считаем историю выходных неполной и делаем полный бэкофилл.
		/// </summary>
		private static bool NeedsFullBackfill ( string symbol )
			{
			return NeedsFullBackfill (symbol, CandleUpdateTf.All);
			}

		private static bool NeedsFullBackfill ( string symbol, CandleUpdateTf tfs )
			{
			bool needsFull = false;

			// 1m + weekend-файл
			if ((tfs & CandleUpdateTf.M1) != 0)
				{
				var path1m = CandlePaths.File (symbol, "1m");
				if (!File.Exists (path1m))
					{
					needsFull = true;
					}
				var weekendPath = CandlePaths.WeekendFile (symbol, "1m");
				if (!File.Exists (weekendPath))
					{
					// weekend-файла нет — историю выходных нужно добрать с нуля
					needsFull = true;
					}
				else
					{
					var weekendStore = new CandleNdjsonStore (weekendPath);
					var firstWeekend = weekendStore.TryGetFirstTimestampUtc ();
					if (!firstWeekend.HasValue || firstWeekend.Value > FullBackfillFromUtc)
						{
						// weekend-файл есть, но начинается позже нужной даты —
						// считаем историю неполной.
						needsFull = true;
						}
					}
				}

			// 1h
			if ((tfs & CandleUpdateTf.H1) != 0)
				{
				var path1h = CandlePaths.File (symbol, "1h");
				if (!File.Exists (path1h))
					{
					needsFull = true;
					}
				}

			// 6h
			if ((tfs & CandleUpdateTf.H6) != 0)
				{
				var path6h = CandlePaths.File (symbol, "6h");
				if (!File.Exists (path6h))
					{
					needsFull = true;
					}
				}

			return needsFull;
			}

		/// <summary>
		/// Обновляет свечи SOL/USDT, BTC/USDT и PAXG/USDT.
		/// Контракт:
		/// - если по символу нет полной истории (любой из включённых TF) → полный бэкофилл с FullBackfillFromUtc;
		/// - иначе догоняются только хвосты по включённым TF.
		/// Любые ошибки из CandleDailyUpdater пробрасываются наверх.
		/// 
		/// Если DebugSkipCandleUpdatesForTests == true, метод просто логирует и выходит,
		/// чтобы тесты не ходили в сеть и не трогали диапазоны дат.
		/// </summary>
		private static async Task UpdateCandlesAsync ( HttpClient http )
			{
			if (DebugSkipCandleUpdatesForTests)
				{
				Console.WriteLine (
					$"[update] DebugSkipCandleUpdatesForTests = true, skipping candle updates (tests). " +
					$"FullBackfillFromUtc={FullBackfillFromUtc:O}");
				return;
				}

			var solSymbol = TradingSymbols.SolUsdtInternal;
			var btcSymbol = TradingSymbols.BtcUsdtInternal;
			var paxgSymbol = TradingSymbols.PaxgUsdtInternal;

			Console.WriteLine (
				$"[update] solSymbol = {solSymbol}, btcSymbol = {btcSymbol}, paxgSymbol = {paxgSymbol}");
			Console.WriteLine ($"[update] FullBackfillFromUtc = {FullBackfillFromUtc:O}");

			// 1. Чистим некорректные/укороченные файлы по каждому символу
			// только по тем TF, которые реально включены.
			if (EnableSolCandleUpdates)
				EnsureSymbolHistoryOrDeleteBad (solSymbol, SolCandleTfs);
			if (EnableBtcCandleUpdates)
				EnsureSymbolHistoryOrDeleteBad (btcSymbol, BtcCandleTfs);
			if (EnablePaxgCandleUpdates)
				EnsureSymbolHistoryOrDeleteBad (paxgSymbol, PaxgCandleTfs);

			// 2. Определяем, нужен ли полный бэкофилл для символа.
			bool solNeedsFull = EnableSolCandleUpdates && NeedsFullBackfill (solSymbol, SolCandleTfs);
			bool btcNeedsFull = EnableBtcCandleUpdates && NeedsFullBackfill (btcSymbol, BtcCandleTfs);
			bool paxgNeedsFull = EnablePaxgCandleUpdates && NeedsFullBackfill (paxgSymbol, PaxgCandleTfs);

			Console.WriteLine (
				$"[update] NeedsFullBackfill: " +
				$"SOL={(EnableSolCandleUpdates ? solNeedsFull : null)}, " +
				$"BTC={(EnableBtcCandleUpdates ? btcNeedsFull : null)}, " +
				$"PAXG={(EnablePaxgCandleUpdates ? paxgNeedsFull : null)}");

			// 3. Создаём апдейтеры с нужным профилем TF.
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
			}
		}
	}
