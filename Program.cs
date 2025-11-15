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
// TODO: Изменить тренировку для SL модели и может быть для A + B модели с почасового таймфрейма на двухчасовой или даже больше, но считать фитиль все равно по часовому таймфрейку. Это должно помочь отфильтровать лишний шум и сделать модель более стабильной.

// TODO: Добавить не часовоую проверку для TP и SL и ликвидации, а пятиминутную внутри дня, чтобы точнее понимать, где именно сработал TP или SL. Сейчас мы смотрим только на часовые свечи, что не всегда точно, так как они сглаживают движение цены внутри часа.ХОТЯ ХЗ нужно ли это вообще