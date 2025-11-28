namespace SolSignalModel1D_Backtest.Core.Analytics.CurrentPrediction
	{
	/// <summary>
	/// Снимок "текущего прогноза":
	/// - одна последняя PredictionRecord;
	/// - forward-метрики (если есть);
	/// - торговые планы по всем политикам и веткам BASE/ANTI-D.
	/// </summary>
	public sealed class CurrentPredictionSnapshot
		{
		public DateTime GeneratedAtUtc { get; set; }

		public DateTime PredictionDateUtc { get; set; }

		/// <summary>Сырый предсказанный класс (0/1/2).</summary>
		public int PredLabel { get; set; }

		/// <summary>Человекочитаемое представление класса (например, "0 (down)").</summary>
		public string PredLabelDisplay { get; set; } = string.Empty;

		/// <summary>Строка по микросигналу (например, "micro UP" или "не используется").</summary>
		public string MicroDisplay { get; set; } = string.Empty;

		public bool RegimeDown { get; set; }

		public double SlProb { get; set; }

		public bool SlHighDecision { get; set; }

		public double Entry { get; set; }

		public double MinMove { get; set; }

		/// <summary>Диагностическое объяснение/причина (если используется).</summary>
		public string Reason { get; set; } = string.Empty;

		/// <summary>Forward 24h по базовому горизонту (если в PredictionRecord есть данные).</summary>
		public Forward24hSnapshot? Forward24h { get; set; }

		/// <summary>Объём кошелька, под который считались размеры позиций.</summary>
		public double WalletBalanceUsd { get; set; }

		/// <summary>Плоский список строк по политикам и веткам BASE/ANTI-D.</summary>
		public List<CurrentPredictionPolicyRow> PolicyRows { get; } = new ();
		}

	/// <summary>
	/// Forward-метрики по базовому горизонту (сейчас — 24h).
	/// </summary>
	public sealed class Forward24hSnapshot
		{
		public double MaxHigh { get; set; }
		public double MinLow { get; set; }
		public double Close { get; set; }
		}

	/// <summary>
	/// Одна строка по политике и ветке (BASE или ANTI-D).
	/// Это то, что дальше уйдёт и в консоль, и в ReportDocument.
	/// </summary>
	public sealed class CurrentPredictionPolicyRow
		{
		public string PolicyName { get; set; } = string.Empty;

		/// <summary>Ветка: "BASE" или "ANTI-D".</summary>
		public string Branch { get; set; } = string.Empty;

		public bool IsRiskDay { get; set; }

		public bool HasDirection { get; set; }

		public bool Skipped { get; set; }

		/// <summary>Направление сделки: "LONG", "SHORT" или "-".</summary>
		public string Direction { get; set; } = string.Empty;

		public double Leverage { get; set; }

		public double Entry { get; set; }

		public double? SlPct { get; set; }

		public double? TpPct { get; set; }

		public double? SlPrice { get; set; }

		public double? TpPrice { get; set; }

		public double? PositionUsd { get; set; }

		public double? PositionQty { get; set; }

		public double? LiqPrice { get; set; }

		public double? LiqDistPct { get; set; }
		}
	}
