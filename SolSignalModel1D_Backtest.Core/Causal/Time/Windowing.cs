using System;
using System.Collections.Generic;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;

namespace SolSignalModel1D_Backtest.Core.Causal.Time
	{
	/// <summary>
	/// Контракт NY-окон для дневной логики:
	/// - входы всегда UTC;
	/// - weekend-entry не имеет baseline-exit (время закрытия окна не определено);
	/// - NY-morning бар: 07:00 зимой / 08:00 летом (DST) по NY локальному времени;
	/// - baseline-exit: следующее NY-утро минус 2 минуты.
	/// </summary>
	public static class Windowing
		{
		public static TimeZoneInfo NyTz => TimeZones.NewYork;

		public static bool IsNyMorning ( DateTime utc, TimeZoneInfo nyTz )
			{
			EnsureUtc (utc, nameof (utc));
			if (nyTz == null) throw new ArgumentNullException (nameof (nyTz));

			var local = TimeZoneInfo.ConvertTimeFromUtc (utc, nyTz);
			if (local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
				return false;

			int expectedHour = nyTz.IsDaylightSavingTime (local) ? 8 : 7;

			return local.Hour == expectedHour
				   && local.Minute == 0
				   && local.Second == 0;
			}

		public static bool IsNyMorning ( DateTime utc ) => IsNyMorning (utc, NyTz);

		public static DateTime ComputeBaselineExitUtc ( DateTime entryUtc ) =>
			ComputeBaselineExitUtc (entryUtc, NyTz);

		public static DateTime ComputeBaselineExitUtc ( DateTime entryUtc, TimeZoneInfo nyTz )
			{
			if (!TryComputeBaselineExitUtc (entryUtc, nyTz, out var exitUtc))
				throw new InvalidOperationException ($"[windowing] Weekend entry is not allowed: {entryUtc:O}.");

			return exitUtc;
			}

		/// <summary>
		/// Вариант для сегментации: если entry попал на weekend по NY локальному времени,
		/// baseline-exit отсутствует и день должен быть вынесен в excluded-сегмент.
		/// </summary>
		public static bool TryComputeBaselineExitUtc ( DateTime entryUtc, TimeZoneInfo nyTz, out DateTime exitUtc )
			{
			EnsureUtc (entryUtc, nameof (entryUtc));
			if (nyTz == null) throw new ArgumentNullException (nameof (nyTz));

			var entryLocal = TimeZoneInfo.ConvertTimeFromUtc (entryUtc, nyTz);

			if (entryLocal.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
				{
				exitUtc = default;
				return false;
				}

			int addDays = entryLocal.DayOfWeek == DayOfWeek.Friday ? 3 : 1;
			var targetDate = entryLocal.Date.AddDays (addDays);

			var noon = new DateTime (targetDate.Year, targetDate.Month, targetDate.Day, 12, 0, 0, DateTimeKind.Unspecified);
			bool dst = nyTz.IsDaylightSavingTime (noon);

			int morningHour = dst ? 8 : 7;
			var nextMorningLocal = new DateTime (
				targetDate.Year, targetDate.Month, targetDate.Day,
				morningHour, 0, 0,
				DateTimeKind.Unspecified);

			var exitLocal = nextMorningLocal.AddMinutes (-2);
			exitUtc = TimeZoneInfo.ConvertTimeToUtc (exitLocal, nyTz);

			if (exitUtc <= entryUtc)
				throw new InvalidOperationException ($"[windowing] Invalid baseline window: start={entryUtc:O}, end={exitUtc:O}.");

			return true;
			}

		/// <summary>
		/// Утилита для тестов/подготовки данных: оставить только те 6h-бары, которые соответствуют NY-morning.
		/// </summary>
		public static List<Candle6h> FilterNyMorningOnly ( IReadOnlyList<Candle6h> candles, TimeZoneInfo nyTz )
			{
			if (candles == null) throw new ArgumentNullException (nameof (candles));
			if (nyTz == null) throw new ArgumentNullException (nameof (nyTz));

			var res = new List<Candle6h> (candles.Count);
			for (int i = 0; i < candles.Count; i++)
				{
				var c = candles[i];
				if (IsNyMorning (c.OpenTimeUtc, nyTz))
					res.Add (c);
				}

			return res;
			}

		/// <summary>
		/// Утилита для тестов: взять blocks блоков с конца, каждый блок длины take,
		/// между блоками пропускать skip элементов. Порядок в результате сохраняется (от старых к новым).
		/// </summary>
		public static List<T> BuildSpacedTest<T> (
			IReadOnlyList<T> rows,
			int take,
			int skip,
			int blocks,
			Func<T, DateTime> dateSelector )
			{
			if (rows == null) throw new ArgumentNullException (nameof (rows));
			if (dateSelector == null) throw new ArgumentNullException (nameof (dateSelector));
			if (take <= 0) throw new ArgumentOutOfRangeException (nameof (take), take, "take must be > 0.");
			if (skip < 0) throw new ArgumentOutOfRangeException (nameof (skip), skip, "skip must be >= 0.");
			if (blocks <= 0) throw new ArgumentOutOfRangeException (nameof (blocks), blocks, "blocks must be > 0.");

			if (rows.Count == 0)
				return new List<T> ();

			var pickedBlocks = new List<List<T>> (blocks);

			int idx = rows.Count;
			for (int b = 0; b < blocks; b++)
				{
				int end = idx;
				int start = Math.Max (0, end - take);

				var block = new List<T> (end - start);
				for (int i = start; i < end; i++)
					block.Add (rows[i]);

				pickedBlocks.Add (block);

				idx = start - skip;
				if (idx <= 0)
					break;
				}

			pickedBlocks.Reverse ();

			var res = new List<T> ();
			foreach (var block in pickedBlocks)
				res.AddRange (block);

			return res;
			}

		/// <summary>
		/// Разбить последовательность на подряд идущие блоки фиксированного размера.
		/// </summary>
		public static IEnumerable<List<T>> GroupByBlocks<T> ( IReadOnlyList<T> rows, int blockSize )
			{
			if (rows == null) throw new ArgumentNullException (nameof (rows));
			if (blockSize <= 0) throw new ArgumentOutOfRangeException (nameof (blockSize), blockSize, "blockSize must be > 0.");

			for (int i = 0; i < rows.Count; i += blockSize)
				{
				int len = Math.Min (blockSize, rows.Count - i);
				var block = new List<T> (len);

				for (int j = 0; j < len; j++)
					block.Add (rows[i + j]);

				yield return block;
				}
			}

		private static void EnsureUtc ( DateTime dt, string name )
			{
			if (dt.Kind != DateTimeKind.Utc)
				throw new InvalidOperationException ($"[windowing] {name} must be UTC. Got Kind={dt.Kind}.");
			}
		}
	}
