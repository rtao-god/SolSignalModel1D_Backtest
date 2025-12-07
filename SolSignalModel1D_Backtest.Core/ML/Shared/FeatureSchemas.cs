namespace SolSignalModel1D_Backtest.Core.ML.Shared
	{
	/// <summary>
	/// Имена признаков для дневной / микро-модели.
	/// Индекс строго соответствует порядку добавления фич в RowBuilder.Features.
	/// </summary>
	public static class DailyFeatureSchema
		{
		/// <summary>
		/// Количество реально используемых фич (до паддинга нулями).
		/// </summary>
		public const int UsedCount = 24;

		/// <summary>
		/// Имена фич длиной ровно MlSchema.FeatureCount.
		/// Первые UsedCount — реальные признаки, остальные — заглушки "_pad_i".
		/// Такой формат удобно использовать в любых табличках/отчетах.
		/// </summary>
		public static readonly string[] Names = Build ();

		private static string[] Build ()
			{
			var names = new string[MlSchema.FeatureCount];

			// Порядок 1:1 с RowBuilder.Features
			names[0] = "SolRet30";
			names[1] = "BtcRet30";
			names[2] = "SolBtcRet30";
			names[3] = "SolRet1";
			names[4] = "SolRet3";
			names[5] = "BtcRet1";
			names[6] = "BtcRet3";
			names[7] = "FngNorm";
			names[8] = "DxyChg30";
			names[9] = "GoldChg30";
			names[10] = "BtcVs200";
			names[11] = "SolRsiCenteredNorm"; // SolRsiCentered / 100
			names[12] = "RsiSlope3Norm";      // RsiSlope3 / 100
			names[13] = "GapBtcSol1";
			names[14] = "GapBtcSol3";
			names[15] = "RegimeDownFlag";
			names[16] = "AtrPct";
			names[17] = "DynVol";
			names[18] = "Funding";
			names[19] = "OiMln";              // OI / 1e6
			names[20] = "HardRegimeFlag";     // hardRegime == 2
			names[21] = "SolAboveEma50";
			names[22] = "SolEma50vs200";
			names[23] = "BtcEma50vs200";

			// Остальное — паддинг. В PFI у них будет почти нулевая важность.
			for (int i = UsedCount; i < names.Length; i++)
				names[i] = $"_pad_{i}";

			return names;
			}
		}

	/// <summary>
	/// Имена признаков для SL-модели (SlOfflineBuilder/SlFeatureBuilder).
	/// Индексы должны совпадать с порядком записи в фичевом векторе SL.
	/// </summary>
	public static class SlFeatureSchema
		{
		/// <summary>
		/// Количество реально используемых SL-фич (до паддинга).
		/// </summary>
		public const int UsedCount = 11;

		/// <summary>
		/// Имена фич длиной MlSchema.FeatureCount.
		/// Первые UsedCount — реальные признаки, остальные — "_pad_i".
		/// </summary>
		public static readonly string[] Names = Build ();

		private static string[] Build ()
			{
			var names = new string[MlSchema.FeatureCount];

			// Порядок должен совпасть с SlFeatureBuilder:
			// 0: goLong flag
			names[0] = "GoLongFlag";
			// 1: strong signal flag
			names[1] = "StrongSignalFlag";
			// 2: dayMinMove (target-minMove)
			names[2] = "DayMinMove";
			// 3: общий диапазон 6h-окна (high-low)/entry
			names[3] = "Range6hTotal";
			// 4: ранний 2h-диапазон
			names[4] = "Range2hEarly";
			// 5: поздний 2h-диапазон
			names[5] = "Range2hLast";
			// 6: расстояние до high 6h-окна
			names[6] = "DistToHigh6h";
			// 7: расстояние до low 6h-окна
			names[7] = "DistToLow6h";
			// 8: "фитильность" последней 1h-свечи
			names[8] = "Last1hWickiness";
			// 9: час входа, нормированный 0..1
			names[9] = "EntryHourNorm";
			// 10: флаг "день с большим MinMove"
			names[10] = "DayMinMoveHighFlag";

			for (int i = UsedCount; i < names.Length; i++)
				names[i] = $"_pad_{i}";

			return names;
			}
		}

	/// <summary>
	/// Для микро-слоя используются те же фичи, что и для дневной модели.
	/// Отдельный алиас — для явности при вызове анализаторов.
	/// </summary>
	public static class MicroFeatureSchema
		{
		/// <summary>
		/// Имена признаков микро-модели (совпадают с дневной схемой).
		/// </summary>
		public static string[] Names => DailyFeatureSchema.Names;
		}
	}
