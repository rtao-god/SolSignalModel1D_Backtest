using System;

namespace SolSignalModel1D_Backtest.Core.Causal.ML.Micro
	{
	/// <summary>
	/// Состояние микро-прогноза для одного дня.
	/// Используется PredictionEngine и метриками.
	/// </summary>
	public sealed class MicroInfo
		{
		/// <summary>Вообще был ли выдан микро-прогноз.</summary>
		public bool Predicted { get; set; }

		/// <summary>True → прогноз вверх, false → вниз (если Predicted = true).</summary>
		public bool Up { get; set; }

		/// <summary>Можно ли трактовать как "микро up" при оценке.</summary>
		public bool ConsiderUp { get; set; }

		/// <summary>Можно ли трактовать как "микро down" при оценке.</summary>
		public bool ConsiderDown { get; set; }

		/// <summary>Вероятность up-класса (обычно Probability из ML.NET).</summary>
		public float Prob { get; set; }

		/// <summary>
		/// Флаг "прогноз совпал с фактом".
		/// Заполняется только если есть разметка FactMicroUp/FactMicroDown.
		/// </summary>
		public bool Correct { get; set; }
		}
	}
