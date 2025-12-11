using DataRow = SolSignalModel1D_Backtest.Core.Data.DataBuilder.DataRow;

namespace SolSignalModel1D_Backtest.Utils
	{
	/// <summary>
	/// Вспомогательные функции для разбиения дневных строк на train/OOS по дате.
	/// </summary>
	internal static class TrainOosSplitHelper
		{
		/// <summary>
		/// Делит строки на train (Date <= trainUntilUtc) и OOS (Date > trainUntilUtc)
		/// за один проход без дополнительных сортировок.
		/// Входная коллекция не модифицируется.
		/// </summary>
		public static (List<DataRow> Train, List<DataRow> Oos) SplitByTrainBoundary (
			IReadOnlyList<DataRow> allRows,
			DateTime trainUntilUtc )
			{
			if (allRows == null)
				throw new ArgumentNullException (nameof (allRows));

			// Предполагается, что allRows уже примерно отсортированы по дате,
			// но для корректности это не требуется.
			// Начальная ёмкость train задаётся равной размеру входа, чтобы
			// уменьшить число реаллокаций массива списка.
			var train = new List<DataRow> (allRows.Count);
			var oos = new List<DataRow> (); // обычно OOS-хвост заметно меньше train

			for (int i = 0; i < allRows.Count; i++)
				{
				var row = allRows[i];

				// Единое централизованное правило разбиения,
				// чтобы избежать расхождений по граничной дате.
				if (row.Date <= trainUntilUtc)
					train.Add (row);
				else
					oos.Add (row);
				}

			return (train, oos);
			}
		}
	}
