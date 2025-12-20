using System;

namespace SolSignalModel1D_Backtest.Core.Causal.Data
	{
	/// <summary>
	/// Единый способ получить каузальную дату entry в UTC из каузальной строки.
	/// Инвариант: дата обязана быть UTC; скрытые конвертации запрещены, чтобы не маскировать ошибки пайплайна.
	/// </summary>
	public static class LabeledCausalRowExtensions
		{
		public static DateTime ToCausalDateUtc ( this LabeledCausalRow r )
			{
			if (r == null)
				throw new ArgumentNullException (nameof (r));

			if (r.Causal == null)
				{
				throw new InvalidOperationException (
					"[causal-row] LabeledCausalRow.Causal is null. " +
					"Это нарушение инвариантов сборки данных (строка невалидна).");
				}

			var dt = r.Causal.DateUtc;

			if (dt.Kind != DateTimeKind.Utc)
				{
				throw new InvalidOperationException (
					$"[causal-row] Causal.DateUtc must be UTC, got Kind={dt.Kind}, Date={dt:O}. " +
					"Нельзя продолжать, иначе Train/OOS границы станут недетерминированными.");
				}

			return dt;
			}
		}
	}
