namespace SolSignalModel1D_Backtest.Api.Dto
	{
	/// <summary>
	/// DTO для отдачи baseline-конфига бэктеста наружу.
	/// Упрощённое отображение BacktestConfig без ссылок на реальные политики.
	/// </summary>
	public sealed class BacktestConfigDto
		{
		public double DailyStopPct { get; set; }
		public double DailyTpPct { get; set; }
		public List<PolicyConfigDto> Policies { get; set; } = new ();
		}

	public sealed class PolicyConfigDto
		{
		public string Name { get; set; } = string.Empty;
		public string PolicyType { get; set; } = string.Empty;
		public double? Leverage { get; set; }
		public string MarginMode { get; set; } = string.Empty;
		}
	}
