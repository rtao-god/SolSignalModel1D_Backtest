using System;
using System.Globalization;

namespace SolSignalModel1D_Backtest.Core.Causal.Utils.Format
	{
	/// <summary>
	/// Форматирование чисел для консоли (коротко, с k/m/b).
	/// </summary>
	public static class ConsoleNumberFormatter
		{
		public static string MoneyShort ( double value )
			{
			double abs = Math.Abs (value);
			if (abs < 1_000d)
				return value.ToString ("0.##", CultureInfo.InvariantCulture);

			if (abs < 1_000_000d)
				return (value / 1_000d).ToString ("0.##", CultureInfo.InvariantCulture) + "k";

			if (abs < 1_000_000_000d)
				return (value / 1_000_000d).ToString ("0.##", CultureInfo.InvariantCulture) + "m";

			return (value / 1_000_000_000d).ToString ("0.##", CultureInfo.InvariantCulture) + "b";
			}

		/// <summary>
		/// Обычный процент, 2 знака.
		/// </summary>
		public static string Pct ( double value )
			{
			return value.ToString ("0.00", CultureInfo.InvariantCulture) + "%";
			}

		/// <summary>
		/// Короткий процент: 123.4%, 2.3k%, 1.2m%.
		/// </summary>
		public static string PctShort ( double value )
			{
			double abs = Math.Abs (value);
			if (abs < 1_000d)
				return value.ToString ("0.##", CultureInfo.InvariantCulture) + "%";
			if (abs < 1_000_000d)
				return (value / 1_000d).ToString ("0.##", CultureInfo.InvariantCulture) + "k%";
			return (value / 1_000_000d).ToString ("0.##", CultureInfo.InvariantCulture) + "m%";
			}

		/// <summary>
		/// Для коэффициентов типа calmar/sharpe: если >1000 → k.
		/// </summary>
		public static string RatioShort ( double value )
			{
			double abs = Math.Abs (value);
			if (abs < 1_000d)
				return value.ToString ("0.###", CultureInfo.InvariantCulture);
			return (value / 1_000d).ToString ("0.###", CultureInfo.InvariantCulture) + "k";
			}

		public static string Plain ( double value, int decimals = 2 )
			{
			string fmt = "0." + new string ('#', decimals);
			return value.ToString (fmt, CultureInfo.InvariantCulture);
			}
		}
	}
