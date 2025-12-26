namespace SolSignalModel1D_Backtest.Core.Backtest.Services
	{
	/// <summary>
	/// Контракт для сервиса, который собирает все исходные данные для бэктеста:
	/// - обновляет/читает свечи;
	/// - строит дневные строки;
	/// - прогоняет модели и формирует PredictionRecord;
	/// - возвращает snapshot, пригодный для BacktestEngine/BacktestPreviewService.
	/// Реализация должна повторять существующий пайплайн из консольного Program.Main.
	/// </summary>
	public interface IBacktestDataProvider
		{
		/// <summary>
		/// Загружает и подготавливает данные для бэктеста/превью.
		/// Ожидается, что здесь используется тот же код, что и в baseline-бэктесте.
		/// </summary>
		Task<BacktestDataSnapshot> LoadAsync ( CancellationToken cancellationToken = default );
		}
	}
