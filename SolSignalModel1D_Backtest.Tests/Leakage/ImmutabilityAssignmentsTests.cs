using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.Leakage
	{
	/// <summary>
	/// Тест, который сканирует всё решение на предмет присваиваний
	/// к полям, которые считаются иммутабельными после конструирования.
	/// Никакой магии Roslyn: только текстовый поиск + простая эвристика.
	/// </summary>
	public sealed class ImmutabilityAssignmentsTests
		{
		/// <summary>
		/// Регэксп для поиска присваиваний вида "obj.Prop = ..." без учёта "==" / "=>".
		/// group 1 = левый operand (например, Causal.TrueLabel).
		/// </summary>
		private static readonly Regex AssignmentRegex =
			new (@"\b([A-Za-z_][A-Za-z0-9_\.]*)\s*=\s*(?![=])", RegexOptions.Compiled);

		/// <summary>
		/// Карта "имя поля в коде" → "человекочитаемый комментарий".
		/// Сюда кладём только те поля, которые нельзя трогать после конструирования.
		/// </summary>
		private static readonly Dictionary<string, string> ImmutableFields = new ()
		{
            // ForwardOutcomes
            { "Forward.Entry",        "ForwardOutcomes.Entry должен задаваться только при создании записи." },
			{ "Forward.MaxHigh24",    "ForwardOutcomes.MaxHigh24 должен задаваться только при создании записи." },
			{ "Forward.MinLow24",     "ForwardOutcomes.MinLow24 должен задаваться только при создании записи." },
			{ "Forward.Close24",      "ForwardOutcomes.Close24 должен задаваться только при создании записи." },
			{ "Forward.MinMove",      "ForwardOutcomes.MinMove должен задаваться только при создании записи." },
			{ "Forward.WindowEndUtc", "ForwardOutcomes.WindowEndUtc должен задаваться только при создании записи." },
			{ "Forward.DayMinutes",   "ForwardOutcomes.DayMinutes должен задаваться только при создании записи." },

            // CausalPredictionRecord — базовые факты и дневной слой:
            { "Causal.DateUtc",          "Дата дня должна задаваться только при построении CausalPredictionRecord." },
			{ "Causal.TrueLabel",        "TrueLabel задаётся один раз при построении CausalPredictionRecord." },
			{ "Causal.FactMicroUp",      "FactMicroUp — факт, не должен меняться в пайплайне." },
			{ "Causal.FactMicroDown",    "FactMicroDown — факт, не должен меняться в пайплайне." },
			{ "Causal.RegimeDown",       "RegimeDown — режим рынка, фиксируется при построении строки." },
			{ "Causal.MinMove",          "MinMove — forward-метрика, не должна переписываться." },
			{ "Causal.ProbUp_Day",       "Prob*_Day — чистый дневной слой, не должен меняться после prediction." },
			{ "Causal.ProbFlat_Day",     "Prob*_Day — чистый дневной слой, не должен меняться после prediction." },
			{ "Causal.ProbDown_Day",     "Prob*_Day — чистый дневной слой, не должен меняться после prediction." },
			{ "Causal.ProbUp_DayMicro",  "Prob*_DayMicro — слой Day+Micro до SL, не должен переписываться." },
			{ "Causal.ProbFlat_DayMicro","Prob*_DayMicro — слой Day+Micro до SL, не должен переписываться." },
			{ "Causal.ProbDown_DayMicro","Prob*_DayMicro — слой Day+Micro до SL, не должен переписываться." },
			{ "Causal.Conf_Day",         "Conf_Day — уверенность дневной модели, не переписывается." },
			{ "Causal.Conf_Micro",       "Conf_Micro — уверенность микро-слоя, не переписывается." },
			{ "Causal.MicroPredicted",   "Флаг MicroPredicted фиксируется на этапе предикта." },
			{ "Causal.PredMicroUp",      "PredMicroUp задаётся только дневной/микро-моделью." },
			{ "Causal.PredMicroDown",    "PredMicroDown задаётся только дневной/микро-моделью." },
			{ "Causal.Reason",           "Reason — объяснение модели, не переписывается дальше." }
		};

		/// <summary>
		/// Простая белая зона: файлы, где допустимо одноразовое присваивание
		/// иммутабельных полей (обычно там, где строятся записи).
		/// По хорошему, тут должны остаться только builder'ы.
		/// </summary>
		private static readonly HashSet<string> AllowedImmutableAssignmentFiles =
			new (StringComparer.OrdinalIgnoreCase)
			{
                // Эти имена нужно подогнать под реальные.
                // Логика: здесь создаются Causal/Forward/BacktestRecord.
                "Program.ForwardAndCausal.cs",
				"Program.PredictionRecords.cs",
				"BacktestRecord.cs",
				"ForwardOutcomes.cs",
                // Если есть отдельные builder'ы — добавить сюда:
                // "ForwardOutcomesBuilder.cs",
                // "CausalPredictionRecordBuilder.cs"
            };

		[Fact]
		public void Immutable_fields_are_not_reassigned_outside_builders ()
			{
			var solutionRoot = FindSolutionRoot ();
			var csFiles = Directory.GetFiles (solutionRoot, "*.cs", SearchOption.AllDirectories);

			var violations = new List<string> ();

			foreach (var file in csFiles)
				{
				var fileName = Path.GetFileName (file);

				// Белая зона: здесь разрешаем присваивать иммутабельные поля,
				// т.к. это, как правило, builders / конструкторные слои.
				var isWhitelisted = AllowedImmutableAssignmentFiles.Contains (fileName);

				var text = File.ReadAllText (file);

				foreach (Match m in AssignmentRegex.Matches (text))
					{
					var lhs = m.Groups[1].Value.Trim (); // левый operand до '='

					// Нас интересуют только записи вида "Causal.X" или "Forward.Y".
					if (!lhs.StartsWith ("Causal.", StringComparison.Ordinal) &&
						!lhs.StartsWith ("Forward.", StringComparison.Ordinal))
						{
						continue;
						}

					if (!ImmutableFields.TryGetValue (lhs, out var comment))
						{
						// Поле не в списке жёстко иммутабельных — пропускаем.
						continue;
						}

					if (isWhitelisted)
						{
						// В builder-файле присваивание считается легальным.
						continue;
						}

					// Считаем номер строки для удобства расследования.
					var line = 1 + text.Take (m.Index).Count (c => c == '\n');

					violations.Add (
						$"{fileName}:{line}: запрещено присваивать {lhs}. {comment}");
					}
				}

			if (violations.Count > 0)
				{
				var message = "Найдены пересвоения иммутабельных полей:\n" +
							  string.Join (Environment.NewLine, violations);
				Assert.Fail (message);
				}
			}

		/// <summary>
		/// Поиск корня решения: поднимаемся вверх от bin/Test до директории,
		/// где лежит *.sln или папка с Core-проектами.
		/// </summary>
		private static string FindSolutionRoot ()
			{
			var dir = new DirectoryInfo (AppContext.BaseDirectory);

			while (dir != null)
				{
				var sln = dir.GetFiles ("*.sln").FirstOrDefault ();
				if (sln != null)
					return dir.FullName;

				// Фоллбек: папка, где есть Core-проекты.
				if (dir.GetDirectories ("SolSignalModel1D_Backtest.Core").Any ())
					return dir.FullName;

				dir = dir.Parent;
				}

			throw new InvalidOperationException ("Не удалось найти корень решения (.sln).");
			}
		}
	}
