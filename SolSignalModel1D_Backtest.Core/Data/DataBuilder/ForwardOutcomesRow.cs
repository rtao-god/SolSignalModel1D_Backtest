using System;
using SolSignalModel1D_Backtest.Core.Utils;

namespace SolSignalModel1D_Backtest.Core.Data.DataBuilder
	{
	/// <summary>
	/// Forward-факты (то, что стало известно после начала окна).
	/// Эти поля запрещено использовать при построении causal-фичей.
	/// </summary>
	public sealed class ForwardOutcomesRow
		{
		/// <summary>
		/// Истинный класс (как ты его определяешь в проекте: 0/1/2 или -1/0/+1).
		/// Важно: это ground-truth, поэтому только в outcomes.
		/// </summary>
		public int Label { get; }

		/// <summary>
		/// Forward-доходность/исход, рассчитанный после окна.
		/// </summary>
		public double SolFwd1 { get; }

		// Path-based факт за 24h (на основе 1m)
		// Dir: -1 = down, 0 = flat/нет касания, +1 = up
		public int PathFirstPassDir { get; }
		public DateTime? PathFirstPassTimeUtc { get; }

		/// <summary>Максимальное достижение вверх от entry в долях (0.01 = +1%).</summary>
		public double PathReachedUpPct { get; }

		/// <summary>Максимальное достижение вниз от entry в долях (0.01 = -1%).</summary>
		public double PathReachedDownPct { get; }

		// micro ground-truth
		public bool FactMicroUp { get; }
		public bool FactMicroDown { get; }

		public ForwardOutcomesRow (
			int label,
			double solFwd1,
			int pathFirstPassDir,
			DateTime? pathFirstPassTimeUtc,
			double pathReachedUpPct,
			double pathReachedDownPct,
			bool factMicroUp,
			bool factMicroDown )
			{
			Label = label;
			SolFwd1 = solFwd1;
			PathFirstPassDir = pathFirstPassDir;

			if (pathFirstPassTimeUtc.HasValue)
				PathFirstPassTimeUtc = UtcTime.RequireUtc (pathFirstPassTimeUtc.Value, nameof (pathFirstPassTimeUtc));
			else
				PathFirstPassTimeUtc = null;

			PathReachedUpPct = pathReachedUpPct;
			PathReachedDownPct = pathReachedDownPct;

			FactMicroUp = factMicroUp;
			FactMicroDown = factMicroDown;

			ValidateFinite (solFwd1, nameof (solFwd1));
			ValidateFinite (pathReachedUpPct, nameof (pathReachedUpPct));
			ValidateFinite (pathReachedDownPct, nameof (pathReachedDownPct));
			}

		private static void ValidateFinite ( double x, string name )
			{
			if (double.IsNaN (x) || double.IsInfinity (x))
				throw new InvalidOperationException ($"Non-finite outcome value {name}: {x}.");
			}
		}
	}
