using System.Threading.Tasks;
using SolSignalModel1D_Backtest.Core.Backtest;

namespace SolSignalModel1D_Backtest
	{
	internal class Program
		{
		public static async Task Main ( string[] args )
			{
			var runner = new BacktestRunner ();
			await runner.RunAsync ();
			}
		}
	}
