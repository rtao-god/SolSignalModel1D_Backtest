using SolSignalModel1D_Backtest.Core.Backtest;
using SolSignalModel1D_Backtest.Core.Backtest.Services;
using SolSignalModel1D_Backtest;
using System.Threading;
using System.Threading.Tasks;

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
			return Program.BuildBacktestDataAsync ();
			}
		}
	}
