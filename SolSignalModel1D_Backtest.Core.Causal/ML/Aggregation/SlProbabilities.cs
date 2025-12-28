namespace SolSignalModel1D_Backtest.Core.Causal.ML.Aggregation
	{
	/// <summary>
	/// DTO для вероятностей риск-слоя SL (стоп-лоссы long/short) и их уверенности.
	/// </summary>
	public struct SlProbabilities
		{
		/// <summary>Вероятность срабатывания стоп-лосса для длинной позиции.</summary>
		public double PSlLong { get; set; }

		/// <summary>Вероятность срабатывания стоп-лосса для короткой позиции.</summary>
		public double PSlShort { get; set; }

		/// <summary>Уверенность в оценке риска SL для длинной позиции.</summary>
		public double ConfidenceLong { get; set; }

		/// <summary>Уверенность в оценке риска SL для короткой позиции.</summary>
		public double ConfidenceShort { get; set; }

		/// <summary>
		/// Флаг, что SL-модель дала валидный прогноз для данного дня
		/// (полезно, если для части дней SL не считается).
		/// </summary>
		public bool HasPrediction { get; set; }
		}
	}
