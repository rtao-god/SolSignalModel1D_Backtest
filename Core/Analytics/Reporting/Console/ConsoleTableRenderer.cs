using SolSignalModel1D_Backtest.Core.Utils;
using SolSignalModel1D_Backtest.Reports.Model;

// TODO: Исправить ошибку с namespace
namespace SolSignalModel1D_Backtest.Core.Analytics.Reporting
	{
	public static class ConsoleTableRenderer
		{
		public static void Render ( TableSection section )
			{
			Console.WriteLine (section.Title);
			Console.WriteLine (new string ('-', section.Title.Length));

			var t = new TextTable ();
			t.AddHeader (section.Columns.ToArray ());

			foreach (var row in section.Rows)
				{
				t.AddRow (row.ToArray ());
				}

			t.WriteToConsole ();
			}
		}
	}
