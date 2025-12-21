using System;
using System.Globalization;

namespace SolSignalModel1D_Backtest.Core.Domain.Time
	{
	/// <summary>
	/// Канонический "ключ дня" для каузального пайплайна.
	/// </summary>
	public readonly record struct CausalDayUtc ( DateOnly Value )
		{
		public static CausalDayUtc FromUtc ( DateTime utc )
			{
			if (utc.Kind != DateTimeKind.Utc)
				{
				throw new InvalidOperationException (
					$"[causal-day] expected UTC DateTime, got Kind={utc.Kind}, t={utc:O}.");
				}

			return new CausalDayUtc (DateOnly.FromDateTime (utc));
			}

		/// <summary>
		/// Начало дня (00:00:00Z). Удобно для логов/диапазонов, где ожидается DateTime.
		/// </summary>
		public DateTime StartUtc ()
			=> Value.ToDateTime (TimeOnly.MinValue, DateTimeKind.Utc);

		public override string ToString ()
			=> Value.ToString ("yyyy-MM-dd", CultureInfo.InvariantCulture);
		}
	}
