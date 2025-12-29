using SolSignalModel1D_Backtest.Core.Causal.Analytics.Backtest.ModelStats;
using SolSignalModel1D_Backtest.Core.Causal.Analytics.Backtest.Snapshots.ModelStats;
using SolSignalModel1D_Backtest.Core.Causal.Analytics.Contracts;
using SolSignalModel1D_Backtest.Reports.Model;

namespace SolSignalModel1D_Backtest.Reports.Reporting.Backtest
	{
	/// <summary>
	/// Строит ReportDocument с модельными статистиками бэктеста
	/// на основе BacktestModelStatsSnapshot / BacktestModelStatsMultiSnapshot.
	/// Никакой новой математики не добавляет — только форматирует данные
	/// для фронта в виде key-value и таблиц (simple/technical).
	/// </summary>
	public static class BacktestModelStatsReportBuilder
		{
		/// <summary>
		/// Новый вход: мульти-снимок по сегментам (Full/Train/OOS/Recent).
		/// Используется для фронта, где нужно выбирать сегмент.
		/// </summary>
		public static ReportDocument Build ( BacktestModelStatsMultiSnapshot multi )
			{
			if (multi == null) throw new ArgumentNullException (nameof (multi));

			var now = DateTime.UtcNow;

			var doc = new ReportDocument
				{
				Id = $"backtest-model-stats-{now:yyyyMMdd_HHmmss}",
				Kind = "backtest_model_stats",
				Title = "Модельные статистики бэктеста (multi-segment, SOLUSDT)",
				GeneratedAtUtc = now
				};

			// === Глобальная мета по запуску (RunKind, HasOos, счётчики) ===
			var globalMeta = BuildMultiMetaSection (multi);
			if (globalMeta != null)
				{
				doc.KeyValueSections.Add (globalMeta);
				}

			// === По каждому сегменту: своя мета ===
			foreach (var segment in multi.Segments)
				{
				if (segment == null || segment.Stats == null)
					continue;

				var prefix = GetSegmentTitlePrefix (segment); // например: "[OOS] "

				// Мета конкретного сегмента (границы, размер).
				var segMeta = BuildSegmentMetaSection (segment, prefix);
				if (segMeta != null)
					{
					doc.KeyValueSections.Add (segMeta);
					}

				// Daily confusion (business + technical).
				AddDailyConfusionSections (doc, segment.Stats.Daily, prefix);

				// Trend-direction confusion: simple / technical.
				AddTrendSections (doc, segment.Stats.Trend, prefix);

				// SL-модель: confusion + metrics + threshold sweep.
				AddSlSections (doc, segment.Stats.Sl, prefix);
				}

			return doc;
			}

		/// <summary>
		/// Старый вход: односегментный снимок.
		/// Поведение сохранено, сигнатура не меняется.
		/// </summary>
		public static ReportDocument Build ( BacktestModelStatsSnapshot snapshot )
			{
			if (snapshot == null) throw new ArgumentNullException (nameof (snapshot));

			var doc = new ReportDocument
				{
				Id = $"backtest-model-stats-{snapshot.ToDateUtc:yyyyMMdd}",
				Kind = "backtest_model_stats",
				Title = "Модельные статистики бэктеста (SOLUSDT)",
				GeneratedAtUtc = DateTime.UtcNow
				};

			// === Общая метаинформация по окну, в котором считались статистики ===
			var metaSection = new KeyValueSection
				{
				Title = "Параметры модельных статистик"
				};

			metaSection.Items.Add (new KeyValueItem
				{
				Key = "FromDateUtc",
				Value = snapshot.FromDateUtc.ToString ("O")
				});

			metaSection.Items.Add (new KeyValueItem
				{
				Key = "ToDateUtc",
				Value = snapshot.ToDateUtc.ToString ("O")
				});

			if (snapshot.Sl.HasValue)
				{
				metaSection.Items.Add (new KeyValueItem
					{
					Key = "SlSignalDays",
					Value = snapshot.Sl.Value.Confusion.TotalSignalDays.ToString ()
					});

				metaSection.Items.Add (new KeyValueItem
					{
					Key = "SlOutcomeDays",
					Value = snapshot.Sl.Value.Confusion.TotalOutcomeDays.ToString ()
					});
				}
			else
				{
				metaSection.Items.Add (new KeyValueItem
					{
					Key = "SlStatsMissingReason",
					Value = snapshot.Sl.MissingReason ?? "Missing"
					});
				}

			doc.KeyValueSections.Add (metaSection);

			// === Daily confusion: бизнес (summary) + технарь (матрица) ===
			AddDailyConfusionSections (doc, snapshot.Daily, titlePrefix: null);

			// === Trend-direction confusion: simple / technical ===
			AddTrendSections (doc, snapshot.Trend, titlePrefix: null);

			// === SL confusion + metrics + threshold sweep ===
			AddSlSections (doc, snapshot.Sl, titlePrefix: null);

			return doc;
			}

        // ====== META: multi-snapshot ======
        private static KeyValueSection? BuildMultiMetaSection(BacktestModelStatsMultiSnapshot multi)
        {
            if (multi.Meta == null)
                return null;

            var meta = multi.Meta;

            var section = new KeyValueSection
            {
                Title = "Параметры модельных статистик (multi-segment)"
            };

            section.Items.Add(new KeyValueItem
            {
                Key = "RunKind",
                Value = meta.RunKind.ToString()
            });

            section.Items.Add(new KeyValueItem
            {
                Key = "HasOos",
                Value = meta.HasOos.ToString()
            });

            if (!meta.TrainUntilExitDayKeyUtc.IsDefault)
            {
                section.Items.Add(new KeyValueItem
                {
                    Key = "TrainUntilExitDayKeyUtc",
                    Value = meta.TrainUntilExitDayKeyUtc.Value.ToString("O")
                });

                section.Items.Add(new KeyValueItem
                {
                    Key = "TrainUntilIsoDate",
                    Value = meta.TrainUntilIsoDate
                });
            }

            section.Items.Add(new KeyValueItem
            {
                Key = "TrainRecordsCount",
                Value = meta.TrainRecordsCount.ToString()
            });

            section.Items.Add(new KeyValueItem
            {
                Key = "OosRecordsCount",
                Value = meta.OosRecordsCount.ToString()
            });

            section.Items.Add(new KeyValueItem
            {
                Key = "TotalRecordsCount",
                Value = meta.TotalRecordsCount.ToString()
            });

            section.Items.Add(new KeyValueItem
            {
                Key = "RecentDays",
                Value = meta.RecentDays.ToString()
            });

            section.Items.Add(new KeyValueItem
            {
                Key = "RecentRecordsCount",
                Value = meta.RecentRecordsCount.ToString()
            });

            return section;
        }

        private static KeyValueSection BuildSegmentMetaSection (
			BacktestModelStatsSegmentSnapshot segment,
			string segmentTitlePrefix )
			{
			var section = new KeyValueSection
				{
				Title = $"{segmentTitlePrefix}Сегмент модельных статистик"
				};

			section.Items.Add (new KeyValueItem
				{
				Key = "SegmentKind",
				Value = segment.Kind.ToString ()
				});

			section.Items.Add (new KeyValueItem
				{
				Key = "FromDateUtc",
				Value = segment.FromDateUtc.ToString ("O")
				});

			section.Items.Add (new KeyValueItem
				{
				Key = "ToDateUtc",
				Value = segment.ToDateUtc.ToString ("O")
				});

			section.Items.Add (new KeyValueItem
				{
				Key = "RecordsCount",
				Value = segment.RecordsCount.ToString ()
				});

			return section;
			}

		private static string GetSegmentKey ( BacktestModelStatsSegmentSnapshot segment )
			{
			return segment.Kind switch
				{
					ModelStatsSegmentKind.FullHistory => "FULL",
					ModelStatsSegmentKind.TrainOnly => "TRAIN",
					ModelStatsSegmentKind.OosOnly => "OOS",
					ModelStatsSegmentKind.RecentWindow => "RECENT",
					_ => segment.Kind.ToString ()
					};
			}

		private static string GetSegmentTitlePrefix ( BacktestModelStatsSegmentSnapshot segment )
			{
			// Префикс вида "[OOS] " или "[TRAIN] " для однозначного парсинга на фронте.
			var key = GetSegmentKey (segment);
			return $"[{key}] ";
			}

		// ===== 1) Daily confusion =====

		/// <summary>
		/// Добавляет в документ две секции по дневной путанице:
		/// - бизнес-режим (человеческое summary по классам);
		/// - технарский режим (матрица TRUE x PRED, как confusion).
		/// Для multi-сегментов добавляется префикс к Title (например "[OOS] ").
		/// </summary>
		private static void AddDailyConfusionSections (
			ReportDocument doc,
			DailyConfusionStats daily,
			string? titlePrefix )
			{
			if (doc == null) throw new ArgumentNullException (nameof (doc));
			if (daily == null) throw new ArgumentNullException (nameof (daily));

			var summarySection = BuildDailyBusinessSummarySection (daily, titlePrefix);
			if (summarySection != null)
				{
				doc.TableSections.Add (summarySection);
				}

			var matrixSection = BuildDailyTechnicalMatrixSection (daily, titlePrefix);
			if (matrixSection != null)
				{
				doc.TableSections.Add (matrixSection);
				}
			}

		/// <summary>
		/// Строит "человеческое" описание по классам в виде таблицы:
		/// Class + Summary.
		/// </summary>
		private static TableSection? BuildDailyBusinessSummarySection (
			DailyConfusionStats daily,
			string? titlePrefix )
			{
			if (daily.Rows == null || daily.Rows.Count == 0)
				return null;

			var title = titlePrefix == null
				? "Daily label summary (business)"
				: $"{titlePrefix}Daily label summary (business)";

			var section = new TableSection
				{
				Title = title
				};

			section.Columns.AddRange (new[]
			{
				"Class",
				"Summary"
			});

			var down = FindDailyRowByLabel (daily.Rows, 0);
			var flat = FindDailyRowByLabel (daily.Rows, 1);
			var up = FindDailyRowByLabel (daily.Rows, 2);

			if (down != null && down.Total > 0)
				{
				section.Rows.Add (new List<string>
					{
					"DOWN",
					BuildDailyClassSummary (
						total: down.Total,
						correct: down.Correct,
						missToDown: 0,
						missToFlat: down.Pred1,
						missToUp: down.Pred2)
					});
				}

			if (flat != null && flat.Total > 0)
				{
				section.Rows.Add (new List<string>
					{
					"FLAT",
					BuildDailyClassSummary (
						total: flat.Total,
						correct: flat.Correct,
						missToDown: flat.Pred0,
						missToFlat: 0,
						missToUp: flat.Pred2)
					});
				}

			if (up != null && up.Total > 0)
				{
				section.Rows.Add (new List<string>
					{
					"UP",
					BuildDailyClassSummary (
						total: up.Total,
						correct: up.Correct,
						missToDown: up.Pred0,
						missToFlat: up.Pred1,
						missToUp: 0)
					});
				}

			if (daily.OverallTotal > 0)
				{
				var overallSummary =
					$"Всего {daily.OverallTotal} дней. " +
					$"Попаданий: {daily.OverallCorrect} ({daily.OverallAccuracyPct:0.0}%).";

				section.Rows.Add (new List<string>
					{
					"Overall",
					overallSummary
					});
				}

			return section;
			}

		/// <summary>
		/// Строит технарскую confusion-матрицу TRUE x PRED с процентами и количеством.
		/// </summary>
		private static TableSection? BuildDailyTechnicalMatrixSection (
			DailyConfusionStats daily,
			string? titlePrefix )
			{
			if (daily.Rows == null || daily.Rows.Count == 0)
				return null;

			var title = titlePrefix == null
				? "Daily label confusion (3-class, technical)"
				: $"{titlePrefix}Daily label confusion (3-class, technical)";

			var section = new TableSection
				{
				Title = title
				};

			section.Columns.AddRange (new[]
			{
				"TRUE",
				"Pred DOWN",
				"Pred FLAT",
				"Pred UP",
				"Hit %"
			});

			var down = FindDailyRowByLabel (daily.Rows, 0);
			var flat = FindDailyRowByLabel (daily.Rows, 1);
			var up = FindDailyRowByLabel (daily.Rows, 2);

			if (down != null && down.Total > 0)
				{
				section.Rows.Add (BuildMatrixRow ("DOWN", down));
				}

			if (flat != null && flat.Total > 0)
				{
				section.Rows.Add (BuildMatrixRow ("FLAT", flat));
				}

			if (up != null && up.Total > 0)
				{
				section.Rows.Add (BuildMatrixRow ("UP", up));
				}

			if (daily.OverallTotal > 0)
				{
				var overall = $"{daily.OverallAccuracyPct:0.0}% ({daily.OverallCorrect} / {daily.OverallTotal})";

				section.Rows.Add (new List<string>
					{
					"Overall accuracy",
					string.Empty,
					string.Empty,
					string.Empty,
					overall
					});
				}

			return section;
			}

		private static DailyClassStatsRow? FindDailyRowByLabel ( IReadOnlyList<DailyClassStatsRow> rows, int trueLabel )
			{
			for (int i = 0; i < rows.Count; i++)
				{
				var row = rows[i];
				if (row.TrueLabel == trueLabel)
					return row;
				}

			return null;
			}

		private static string BuildDailyClassSummary (
			int total,
			int correct,
			int missToDown,
			int missToFlat,
			int missToUp )
			{
			if (total <= 0)
				return "Недостаточно данных по этому классу.";

			int misses = total - correct;

			double accPct = (double) correct / total * 100.0;
			double missPct = 100.0 - accPct;

			double missDownPct = (double) missToDown / total * 100.0;
			double missFlatPct = (double) missToFlat / total * 100.0;
			double missUpPct = (double) missToUp / total * 100.0;

			return
				$"Всего {total} дней. " +
				$"Попаданий: {correct} ({accPct:0.0}%). " +
				$"Промахов: {misses} ({missPct:0.0}%). " +
				$"Ошибки: " +
				$"DOWN {missDownPct:0.0}% ({missToDown}), " +
				$"FLAT {missFlatPct:0.0}% ({missToFlat}), " +
				$"UP {missUpPct:0.0}% ({missToUp}).";
			}

		private static List<string> BuildMatrixRow ( string trueName, DailyClassStatsRow row )
			{
			int total = row.Total;

			string Format ( int count )
				{
				if (total <= 0)
					return "—";

				double pct = (double) count / total * 100.0;
				return $"{pct:0.0}% ({count})";
				}

			return new List<string>
				{
				trueName,
				Format (row.Pred0),
				Format (row.Pred1),
				Format (row.Pred2),
				$"{row.AccuracyPct:0.0}%"
				};
			}

		// ===== 2) Trend-direction confusion =====

		private static void AddTrendSections (
			ReportDocument doc,
			TrendDirectionStats trend,
			string? titlePrefix )
			{
			if (doc == null) throw new ArgumentNullException (nameof (doc));
			if (trend == null) throw new ArgumentNullException (nameof (trend));

			var titleSimple = titlePrefix == null
				? "Trend-direction confusion (упрощённо)"
				: $"{titlePrefix}Trend-direction confusion (упрощённо)";

			var titleTechnical = titlePrefix == null
				? "Trend-direction confusion (технически)"
				: $"{titlePrefix}Trend-direction confusion (технически)";

			var trendSimple = MetricTableBuilder.BuildTable (
				BacktestModelStatsTableDefinitions.TrendConfusion,
				trend.Rows,
				TableDetailLevel.Simple,
				explicitTitle: titleSimple);

			var trendTechnical = MetricTableBuilder.BuildTable (
				BacktestModelStatsTableDefinitions.TrendConfusion,
				trend.Rows,
				TableDetailLevel.Technical,
				explicitTitle: titleTechnical);

			doc.TableSections.Add (trendSimple);
			doc.TableSections.Add (trendTechnical);
			}

		// ===== 3) SL-модель =====

		private static void AddSlSections (
			ReportDocument doc,
			OptionalValue<SlStats> sl,
			string? titlePrefix )
			{
			if (doc == null) throw new ArgumentNullException (nameof (doc));
			if (!sl.HasValue)
				{
				var title = titlePrefix == null
					? "SL-model"
					: $"{titlePrefix}SL-model";

				var section = new KeyValueSection
					{
					Title = title
					};

				section.Items.Add (new KeyValueItem
					{
					Key = "MissingReason",
					Value = sl.MissingReason ?? "Missing"
					});

				doc.KeyValueSections.Add (section);
				return;
				}

			var slConf = sl.Value.Confusion;

			var confTitle = titlePrefix == null
				? "SL-model confusion (runtime, path-based)"
				: $"{titlePrefix}SL-model confusion (runtime, path-based)";

			var slConfSection = new TableSection
				{
				Title = confTitle
				};

			slConfSection.Columns.AddRange (new[]
			{
				"day type",
				"pred LOW",
				"pred HIGH"
			});

			slConfSection.Rows.Add (new List<string>
			{
				"TP-day",
				slConf.TpLow.ToString(),
				slConf.TpHigh.ToString()
			});

			slConfSection.Rows.Add (new List<string>
			{
				"SL-day",
				slConf.SlLow.ToString(),
				slConf.SlHigh.ToString()
			});

			slConfSection.Rows.Add (new List<string>
			{
				"SL saved (potential)",
				slConf.SlSaved.ToString(),
				string.Empty
			});

			doc.TableSections.Add (slConfSection);

			var slMetrics = sl.Value.Metrics;

			var metricsTitle = titlePrefix == null
				? "SL-model metrics (runtime)"
				: $"{titlePrefix}SL-model metrics (runtime)";

			var slMetricsSection = new TableSection
				{
				Title = metricsTitle
				};

			slMetricsSection.Columns.AddRange (new[]
			{
				"metric",
				"value"
			});

			slMetricsSection.Rows.Add (new List<string>
			{
				"coverage (scored / signal days)",
				$"{slMetrics.Coverage * 100.0:0.0}%  ({slConf.ScoredDays}/{slConf.TotalSignalDays})"
			});

			slMetricsSection.Rows.Add (new List<string>
			{
				"TPR / Recall (SL-day)",
				$"{slMetrics.Tpr * 100.0:0.0}%"
			});

			slMetricsSection.Rows.Add (new List<string>
			{
				"FPR (TP-day)",
				$"{slMetrics.Fpr * 100.0:0.0}%"
			});

			slMetricsSection.Rows.Add (new List<string>
			{
				"Precision (SL-day)",
				$"{slMetrics.Precision * 100.0:0.0}%"
			});

			slMetricsSection.Rows.Add (new List<string>
			{
				"F1 (SL-day)",
				$"{slMetrics.F1:0.000}"
			});

			slMetricsSection.Rows.Add (new List<string>
			{
				"PR-AUC (approx)",
				$"{slMetrics.PrAuc:0.000}"
			});

			doc.TableSections.Add (slMetricsSection);

			// Threshold sweep: simple / technical (если есть данные)
			if (sl.Value.Thresholds.Count > 0)
				{
				var thrSimpleTitle = titlePrefix == null
					? "SL threshold sweep (упрощённо)"
					: $"{titlePrefix}SL threshold sweep (упрощённо)";

				var thrTechnicalTitle = titlePrefix == null
					? "SL threshold sweep (технически)"
					: $"{titlePrefix}SL threshold sweep (технически)";

				var thrSimple = MetricTableBuilder.BuildTable (
					BacktestModelStatsTableDefinitions.SlThresholdSweep,
					sl.Value.Thresholds,
					TableDetailLevel.Simple,
					explicitTitle: thrSimpleTitle);

				var thrTechnical = MetricTableBuilder.BuildTable (
					BacktestModelStatsTableDefinitions.SlThresholdSweep,
					sl.Value.Thresholds,
					TableDetailLevel.Technical,
					explicitTitle: thrTechnicalTitle);

				doc.TableSections.Add (thrSimple);
				doc.TableSections.Add (thrTechnical);
				}
			}
		}
	}
