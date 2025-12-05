using SolSignalModel1D_Backtest.Core.Backtest;
using SolSignalModel1D_Backtest.Core.Backtest.Services;

namespace SolSignalModel1D_Backtest.Api.Services
	{
	/// <summary>
	/// Провайдер данных для бэктеста/превью в API.
	/// Делегирует сбор данных общему пайплайну Program.BuildBacktestDataAsync(),
	/// чтобы не дублировать сложную логику загрузки свечей, индикаторов и моделей.
	/// </summary>
	public sealed class BacktestDataProvider : IBacktestDataProvider
		{
		public Task<BacktestDataSnapshot> LoadAsync ( CancellationToken cancellationToken = default )
			{
			// Важно: вызываться должен доменный entrypoint BuildBacktestDataAsync,
			// а не внутренний BootstrapDataAsync. Так API зависит только от
			// стабильного контракта BacktestDataSnapshot, а не от технического контейнера.
			return SolSignalModel1D_Backtest.Program.BuildBacktestDataAsync ();
			}
		}
	}
