using System.Collections.Generic;
using SolSignalModel1D_Backtest.Core.Causal.Data;

namespace SolSignalModel1D_Backtest.Core.Causal.Data.DataBuilder
	{
	/// <summary>
	/// Результат построения дневных строк:
	/// - CausalRows: для инференса (без истины);
	/// - LabeledRows: для обучения/оценки (истина отдельно).
	/// </summary>
	public sealed class DailyRowsBuildResult
		{
		public required IReadOnlyList<CausalDataRow> CausalRows { get; init; }
		public required IReadOnlyList<LabeledCausalRow> LabeledRows { get; init; }
		}
	}
