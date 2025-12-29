using SolSignalModel1D_Backtest.Core.Causal.Utils;
using SolSignalModel1D_Backtest.Reports.Model;

namespace SolSignalModel1D_Backtest.Reports.Reporting.ConsoleViews
	{
	public static class ConsoleViewsTableRenderer
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
