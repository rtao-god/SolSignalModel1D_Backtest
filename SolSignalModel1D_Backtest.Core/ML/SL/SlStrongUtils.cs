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
			// Любые NaN/отрицательные/нулевые значения считаем "слабый день"
			// — это безопаснее, чем случайно промаркировать их как сильные.
			if (double.IsNaN (dayMinMove) || dayMinMove <= 0.0)
				return false;

			// Если базовый порог не задан, фактически считаем все дни "сильными".
			// Такой режим можно использовать как деградацию до "старого" поведения.
			if (baseThreshold <= 0.0)
				return true;

			// Коридор вокруг базового порога:
			//   dayMinMove <= 0.8T → явно слабый (очень маленькое ожидаемое движение);
			//   dayMinMove >= 1.2T → явно сильный (волатильный день);
			//   между порогами — серая зона, решаем по режиму.
			double weakCut = baseThreshold * 0.8;
			double strongCut = baseThreshold * 1.2;

			if (dayMinMove <= weakCut)
				return false;

			if (dayMinMove >= strongCut)
				return true;

			// Серая зона:
			// в даун-режиме считаем день "сильным" (риск завышен, хотим аккуратнее обращаться с плечом),
			// в нормальном режиме — остаёмся консервативными и считаем день "слабым".
			return regimeDown;
			}
		}
	}
