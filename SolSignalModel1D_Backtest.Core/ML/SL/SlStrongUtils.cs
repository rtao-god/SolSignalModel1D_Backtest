namespace SolSignalModel1D_Backtest.Core.ML.SL
	{
	/// <summary>
	/// Общая логика интерпретации MinMove → "сильный"/"слабый" день
	/// для SL-модели и всех связанных мест.
	/// Идея:
	///   - baseThreshold (например, 0.03 = 3%) задаёт "типичный" сильный день;
	///   - вокруг него строится коридор [0.8T; 1.2T];
	///   - ниже 0.8T → точно слабый;
	///   - выше 1.2T → точно сильный;
	///   - в серой зоне решаем по режиму (DOWN → считаем сильным, NORMAL → слабым).
	/// </summary>
	public static class SlStrongUtils
		{
		/// <summary>
		/// Решает, считать ли день "сильным" по MinMove.
		/// Используется и при оффлайн-постборе SL-датасета, и в рантайме.
		/// </summary>
		/// <param name="dayMinMove">Адаптивный minMove по дню (0.03 = 3%).</param>
		/// <param name="regimeDown">Флаг даун-режима дневной модели.</param>
		/// <param name="baseThreshold">
		/// Базовый порог minMove, вокруг которого определяется сильный/слабый день.
		/// </param>
		public static bool IsStrongByMinMove ( double dayMinMove, bool regimeDown, double baseThreshold )
			{
			// NaN/Infinity/неположительные значения здесь означают поломку апстрима:
			// либо неверный расчёт minMove, либо дырки в данных.
			// Тихий "false" маскирует проблему и делает диагностику невозможной.
			if (double.IsNaN (dayMinMove) || double.IsInfinity (dayMinMove) || dayMinMove <= 0.0)
				{
				throw new InvalidOperationException (
					$"[sl-strong] dayMinMove is invalid: {dayMinMove}. Expected finite value > 0.");
				}

			// baseThreshold — конфигурационный инвариант.
			// Режим "baseThreshold<=0 => true" превращает ошибку конфигурации в молчаливую деградацию.
			if (double.IsNaN (baseThreshold) || double.IsInfinity (baseThreshold) || baseThreshold <= 0.0)
				{
				throw new InvalidOperationException (
					$"[sl-strong] baseThreshold is invalid: {baseThreshold}. Expected finite value > 0.");
				}

			double weakCut = baseThreshold * 0.8;
			double strongCut = baseThreshold * 1.2;

			if (dayMinMove <= weakCut)
				return false;

			if (dayMinMove >= strongCut)
				return true;

			// Серая зона:
			// в даун-режиме считаем день "сильным" (риск завышен),
			// в нормальном режиме — консервативно считаем день "слабым".
			return regimeDown;
			}
		}
	}
