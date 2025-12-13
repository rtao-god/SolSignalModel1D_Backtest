using System;
using System.Collections.Generic;
using System.Globalization;

namespace SolSignalModel1D_Backtest.Core.Causal.Data
	{
	/// <summary>
	/// Единый "контракт" разбиения train/OOS.
	///
	/// ВАЖНО: правило намеренно основано на baseline-exit, а не на entry-дате.
	/// Причина: дневной таргет/оценка завязаны на forward-окно до baseline-exit,
	/// Любой код, который продолжит сравнивать даты руками (<=/>), считается багом архитектуры:
	/// он создаёт несогласованные сегменты и потенциальный boundary leakage.
	/// </summary>
	public readonly struct TrainBoundary
		{
		private readonly DateTime _trainUntilUtc;
		private readonly TimeZoneInfo _nyTz;

		/// <summary>
		/// Для логов отдаём строку, чтобы в глубине проекта не было соблазна сравнивать DateTime руками.
		/// </summary>
		public string TrainUntilIsoDate => _trainUntilUtc.ToString ("yyyy-MM-dd", CultureInfo.InvariantCulture);

		public TrainBoundary ( DateTime trainUntilUtc, TimeZoneInfo nyTz )
			{
			if (trainUntilUtc == default)
				throw new ArgumentException ("trainUntilUtc must be initialized (non-default).", nameof (trainUntilUtc));

			if (trainUntilUtc.Kind != DateTimeKind.Utc)
				throw new ArgumentException ("trainUntilUtc must be UTC (DateTimeKind.Utc).", nameof (trainUntilUtc));

			_nyTz = nyTz ?? throw new ArgumentNullException (nameof (nyTz));
			_trainUntilUtc = trainUntilUtc;
			}

		/// <summary>
		/// Пытается получить baseline-exit. Для выходных возвращает false (baseline-окна нет по контракту).
		/// Любые другие ошибки Windowing считаются фатальными: это проблема данных/окна.
		/// </summary>
		public bool TryGetBaselineExitUtc ( DateTime entryUtc, out DateTime exitUtc )
			{
			if (entryUtc.Kind != DateTimeKind.Utc)
				throw new ArgumentException ("entryUtc must be UTC (DateTimeKind.Utc).", nameof (entryUtc));

			var ny = TimeZoneInfo.ConvertTimeFromUtc (entryUtc, _nyTz);

			if (ny.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
				{
				exitUtc = default;
				return false;
				}

			exitUtc = Windowing.ComputeBaselineExitUtc (entryUtc, _nyTz);
			return true;
			}

		public bool IsTrainEntry ( DateTime entryUtc )
			{
			if (!TryGetBaselineExitUtc (entryUtc, out var exitUtc))
				return false;

			return exitUtc <= _trainUntilUtc;
			}

		public bool IsOosEntry ( DateTime entryUtc )
			{
			if (!TryGetBaselineExitUtc (entryUtc, out var exitUtc))
				return false;

			return exitUtc > _trainUntilUtc;
			}

		/// <summary>
		/// Единая точка разбиения любых сущностей по entryUtc (DataRow.Date, Record.DateUtc и т.п.).
		/// Excluded — это дни, для которых baseline-exit не определён (обычно weekend).
		/// Их важно не "молча" относить ни к train, ни к OOS.
		/// </summary>
		public TrainOosSplit<T> Split<T> ( IReadOnlyList<T> items, Func<T, DateTime> entryUtcSelector )
			{
			if (items == null) throw new ArgumentNullException (nameof (items));
			if (entryUtcSelector == null) throw new ArgumentNullException (nameof (entryUtcSelector));

			var train = new List<T> (items.Count);
			var oos = new List<T> ();
			var excluded = new List<T> ();

			for (int i = 0; i < items.Count; i++)
				{
				var item = items[i];
				var entryUtc = entryUtcSelector (item);

				if (!TryGetBaselineExitUtc (entryUtc, out var exitUtc))
					{
					excluded.Add (item);
					continue;
					}

				if (exitUtc <= _trainUntilUtc)
					train.Add (item);
				else
					oos.Add (item);
				}

			return new TrainOosSplit<T> (train, oos, excluded);
			}
		}

	public sealed class TrainOosSplit<T>
		{
		public IReadOnlyList<T> Train { get; }
		public IReadOnlyList<T> Oos { get; }
		public IReadOnlyList<T> Excluded { get; }

		public TrainOosSplit ( List<T> train, List<T> oos, List<T> excluded )
			{
			if (train == null) throw new ArgumentNullException (nameof (train));
			if (oos == null) throw new ArgumentNullException (nameof (oos));
			if (excluded == null) throw new ArgumentNullException (nameof (excluded));

			Train = train.AsReadOnly ();
			Oos = oos.AsReadOnly ();
			Excluded = excluded.AsReadOnly ();
			}
		}
	}
