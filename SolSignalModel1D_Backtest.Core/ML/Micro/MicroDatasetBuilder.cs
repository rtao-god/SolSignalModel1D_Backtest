using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;

namespace SolSignalModel1D_Backtest.Core.ML.Micro
	{
	/// <summary>
	/// Датасет для микро-слоя:
	/// - TrainRows — все train-дни;
	/// - MicroRows — только дни с FactMicroUp/FactMicroDown.
	/// </summary>
	public sealed class MicroDataset
		{
		public List<DataRow> TrainRows { get; }
		public List<DataRow> MicroRows { get; }
		public DateTime TrainUntilUtc { get; }

		public MicroDataset (
			List<DataRow> trainRows,
			List<DataRow> microRows,
			DateTime trainUntilUtc )
			{
			TrainRows = trainRows ?? throw new ArgumentNullException (nameof (trainRows));
			MicroRows = microRows ?? throw new ArgumentNullException (nameof (microRows));
			TrainUntilUtc = trainUntilUtc;
			}
		}

	/// <summary>
	/// Dataset-builder для микро-слоя:
	/// режет по trainUntil и выбирает размеченные микро-дни.
	/// </summary>
	public static class MicroDatasetBuilder
		{
		public static MicroDataset Build (
			List<DataRow> allRows,
			DateTime trainUntil )
			{
			if (allRows == null) throw new ArgumentNullException (nameof (allRows));

			var ordered = allRows
				.OrderBy (r => r.Date)
				.ToList ();

			var trainRows = ordered
				.Where (r => r.Date <= trainUntil)
				.ToList ();

			var microRows = trainRows
				.Where (r => r.FactMicroUp || r.FactMicroDown)
				.OrderBy (r => r.Date)
				.ToList ();

			return new MicroDataset (trainRows, microRows, trainUntil);
			}
		}
	}
