namespace SolSignalModel1D_Backtest.Api.CurrentPrediction
	{
	/// <summary>
	/// ДТО верхнего уровня для REST-эндпоинта "текущий прогноз".
	/// Эта структура уходит на фронтенд 1:1 в виде JSON.
	/// </summary>
	public sealed class CurrentPredictionDto
		{
		/// <summary>
		/// UTC-время прогноза (момент 6h-свечи входа).
		/// </summary>
		public DateTime TimestampUtc { get; set; }

		/// <summary>
		/// Локальное время Нью-Йорка для этого прогноза.
		/// </summary>
		public DateTime TimestampNy { get; set; }

		/// <summary>
		/// Текущее время в Тбилиси (для контекста).
		/// </summary>
		public DateTime NowTbilisi { get; set; }

		/// <summary>
		/// Блок с информацией о самом прогнозе (класс, микро, режим, SL-вероятность).
		/// </summary>
		public CurrentPredictionInfoDto Prediction { get; set; } = new ();

		/// <summary>
		/// Forward-статистика по базовому горизонту NY→следующее NY-утро.
		/// </summary>
		public CurrentPredictionForwardDto ForwardBaseline { get; set; } = new ();

		/// <summary>
		/// Список политик (RiskAware, Const3x и т.д.) с ветками BASE/ANTI-D.
		/// </summary>
		public List<CurrentPredictionPolicyDto> Policies { get; set; } = new ();
		}

	/// <summary>
	/// Информация о прогнозе по последнему дню.
	/// </summary>
	public sealed class CurrentPredictionInfoDto
		{
		public int ClassCode { get; set; }
		public string ClassLabel { get; set; } = string.Empty;

		public string Micro { get; set; } = string.Empty;

		public string Regime { get; set; } = string.Empty;

		public double SlProb { get; set; }

		public bool SlHighDecision { get; set; }

		public double EntryPrice { get; set; }

		public double MinMove { get; set; }
		}

	/// <summary>
	/// Forward по базовому выходу:
	/// max/high/low/close в окне entry→baseline exit.
	/// </summary>
	public sealed class CurrentPredictionForwardDto
		{
		/// <summary>
		/// Можно ли честно посчитать forward (хватает свечей).
		/// </summary>
		public bool HasForward { get; set; }

		public double? MaxHigh { get; set; }

		public double? MinLow { get; set; }

		public double? CloseExit { get; set; }

		/// <summary>
		/// Сообщение для ошибок/края данных (опционально).
		/// </summary>
		public string? Message { get; set; }
		}

	/// <summary>
	/// Описание одной политики и её двух веток (BASE и ANTI-D).
	/// </summary>
	public sealed class CurrentPredictionPolicyDto
		{
		/// <summary>
		/// Машинный идентификатор политики (например, "risk_aware" или "const_3x").
		/// </summary>
		public string Id { get; set; } = string.Empty;

		/// <summary>
		/// Читабельное имя политики (по типу/конфигу).
		/// </summary>
		public string Name { get; set; } = string.Empty;

		/// <summary>
		/// Баланс кошелька, на который рассчитаны числа (маржа).
		/// </summary>
		public double WalletBalanceUsd { get; set; }

		/// <summary>
		/// Ветка BASE direction: нерискованные дни по SL.
		/// </summary>
		public CurrentPredictionPolicyBranchDto Base { get; set; } = new ();

		/// <summary>
		/// Ветка ANTI-D overlay: только рискованные дни по SL.
		/// </summary>
		public CurrentPredictionPolicyBranchDto AntiD { get; set; } = new ();
		}

	/// <summary>
	/// Одна ветка политики (BASE / ANTI-D).
	/// </summary>
	public sealed class CurrentPredictionPolicyBranchDto
		{
		/// <summary>
		/// Статус ветки:
		/// "ok" — есть сделка,
		/// "flat" — дневной сигнал без направления,
		/// "skipped" — день пропущен по логике ветки.
		/// </summary>
		public string Status { get; set; } = string.Empty;

		/// <summary>
		/// Причина, если ветка не торгует (flat/skip).
		/// </summary>
		public string? Reason { get; set; }

		/// <summary>
		/// Данные о сделке, если Status == "ok".
		/// </summary>
		public CurrentPredictionTradeDto? Trade { get; set; }
		}

	/// <summary>
	/// Конкретный торговый план для ветки:
	/// направление, плечо, SL/TP, размер позиции и ликвидация.
	/// </summary>
	public sealed class CurrentPredictionTradeDto
		{
		public string Direction { get; set; } = string.Empty; // "LONG" / "SHORT"

		public double Leverage { get; set; }

		public double EntryPrice { get; set; }

		public double SlPrice { get; set; }

		public double SlPct { get; set; }

		public double TpPrice { get; set; }

		public double TpPct { get; set; }

		public double PositionQty { get; set; }

		public double PositionUsd { get; set; }

		public double WalletBalanceUsd { get; set; }

		public double? LiqPrice { get; set; }

		public double? LiqDistancePct { get; set; }
		}
	}
