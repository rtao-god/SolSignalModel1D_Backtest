using System;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;

namespace SolSignalModel1D_Backtest.Core.Utils.Time
	{
	/// <summary>
	/// Единый явный контракт времени для каузального пайплайна.
	///
	/// Важно различать:
	/// - EntryUtc: реальный момент входа (timestamp), используется для окон (1h/1m), словарей по OpenTimeUtc и т.п.
	/// - DayKeyUtc: ключ дня (date-only в UTC), используется для Train/OOS границ, агрегаций и сопоставления дней.
	/// </summary>
	public static class CausalTimeKey
		{
		public static DateTime EntryUtc ( BacktestRecord r )
			{
			if (r == null) throw new ArgumentNullException (nameof (r));
			if (r.Causal == null)
				throw new InvalidOperationException ("[time] BacktestRecord.Causal is null (invalid record).");

			var t = r.Causal.DateUtc;
			if (t.Kind != DateTimeKind.Utc)
				throw new InvalidOperationException ($"[time] BacktestRecord.Causal.DateUtc must be UTC. Got Kind={t.Kind}, t={t:O}.");

			return t;
			}

		public static DateTime EntryUtc ( LabeledCausalRow r )
			{
			if (r == null) throw new ArgumentNullException (nameof (r));
			if (r.Causal == null)
				throw new InvalidOperationException ("[time] LabeledCausalRow.Causal is null (invalid row).");

			var t = r.Causal.DateUtc;
			if (t.Kind != DateTimeKind.Utc)
				throw new InvalidOperationException ($"[time] LabeledCausalRow.Causal.DateUtc must be UTC. Got Kind={t.Kind}, t={t:O}.");

			return t;
			}

		public static DateTime DayKeyUtc ( BacktestRecord r )
			{
			// DayKey = нормализованный ключ дня; ToCausalDateUtc() должен быть определён для DateTime.
			return EntryUtc (r).ToCausalDateUtc ();
			}

		public static DateTime DayKeyUtc ( LabeledCausalRow r )
			{
			return EntryUtc (r).ToCausalDateUtc ();
			}
		}
	}
