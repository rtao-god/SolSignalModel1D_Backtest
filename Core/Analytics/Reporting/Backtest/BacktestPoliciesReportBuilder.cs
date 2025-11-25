using SolSignalModel1D_Backtest.Core.Analytics.Backtest.Reporting;
using SolSignalModel1D_Backtest.Core.Analytics.Reporting;
using SolSignalModel1D_Backtest.Reports.Model;

namespace SolSignalModel1D_Backtest.Core.Analytics.Backtest.Reports
	{
	/// <summary>
	/// Строит табличные представления результатов политик для разных аудиторий.
	/// </summary>
	public static class BacktestPoliciesReportBuilder
		{
		/// <summary>
		/// Строит две таблицы:
		/// - simple: короткая для "продажи";
		/// - technical: расширенная для технарей.
		/// </summary>
		public static (TableSection Simple, TableSection Technical) BuildPolicyTables (
			IReadOnlyList<BacktestPolicyResult> policyResults )
			{
			// Простая таблица.
			var simple = MetricTableBuilder.BuildTable (
				BacktestPolicyTableDefinitions.Policies,
				policyResults,
				TableDetailLevel.Simple,
				explicitTitle: "Политики (упрощённо)");

			// Технарская таблица.
			var technical = MetricTableBuilder.BuildTable (
				BacktestPolicyTableDefinitions.Policies,
				policyResults,
				TableDetailLevel.Technical,
				explicitTitle: "Политики (технические детали)");

			return (simple, technical);
			}
		}
	}
