namespace SolSignalModel1D_Backtest.Core.Backtest.Profiles
	{
	/// <summary>
	/// Репозиторий профилей бэктеста (BacktestProfile).
	/// 
	/// Инварианты:
	/// - всегда можно считать список профилей (GetAllAsync);
	/// - baseline-профиль должен существовать (репозиторий сам его создаёт при необходимости);
	/// - SaveAsync работает как upsert: создаёт новый профиль или обновляет существующий по Id.
	/// </summary>
	public interface IBacktestProfileRepository
		{
		/// <summary>
		/// Возвращает все профили (включая baseline).
		/// Репозиторий сам может создавать baseline, если файла ещё нет.
		/// </summary>
		Task<IReadOnlyList<BacktestProfile>> GetAllAsync (
			CancellationToken cancellationToken = default );

		/// <summary>
		/// Возвращает профиль по идентификатору или null, если не найден.
		/// </summary>
		Task<BacktestProfile?> GetByIdAsync (
			string id,
			CancellationToken cancellationToken = default );

		/// <summary>
		/// Создаёт или обновляет профиль.
		/// Если профиль с таким Id уже есть — заменяет его.
		/// Если нет — добавляет новый.
		/// Возвращает сохранённую версию.
		/// </summary>
		Task<BacktestProfile> SaveAsync (
			BacktestProfile profile,
			CancellationToken cancellationToken = default );
		}
	}
