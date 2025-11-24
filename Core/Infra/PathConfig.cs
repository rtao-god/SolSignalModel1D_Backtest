using System;
using System.IO;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.Infra
	{
	/// <summary>
	/// Централизованная конфигурация путей. Кэширование данных.
	/// Правила:
	/// 1) env SSM_CACHE_ROOT — если задан, используем его.
	/// 2) иначе ищем корень репозитория (папка с .git или *.sln) от CWD вверх,
	///    при этом .git имеет приоритет над .sln.
	/// 3) fallback: текущая директория.
	/// Итог: cache/ под корнем.
	/// </summary>
	public static class PathConfig
		{
		public static string CacheRoot
			{
			get
				{
				var env = Environment.GetEnvironmentVariable ("SSM_CACHE_ROOT");
				if (!string.IsNullOrWhiteSpace (env))
					return EnsureDir (env);

				var repo = FindRepoRootFrom (Directory.GetCurrentDirectory ());
				var root = Path.Combine (repo, "cache");
				return EnsureDir (root);
				}
			}

		public static string CandlesDir => EnsureDir (Path.Combine (CacheRoot, "candles"));
		public static string IndicatorsDir => EnsureDir (Path.Combine (CacheRoot, "indicators"));

		/// <summary>
		/// Каталог профилей бэктеста.
		/// Располагается рядом с cache:
		///   <repo-root>/cache
		///   <repo-root>/profiles
		/// Если родитель cache не найден (маловероятно) — будет под CacheRoot.
		/// </summary>
		public static string ProfilesDir
			{
			get
				{
				var cacheRoot = CacheRoot;
				var cacheDirInfo = new DirectoryInfo (cacheRoot);

				// Пытаемся подняться на уровень выше cache.
				var repoRoot = cacheDirInfo.Parent?.FullName ?? cacheRoot;

				var profiles = Path.Combine (repoRoot, "profiles");
				return EnsureDir (profiles);
				}
			}

		private static string EnsureDir ( string p )
			{
			Directory.CreateDirectory (p);
			return p;
			}

		/// <summary>
		/// Ищет корень репозитория, двигаясь вверх от start.
		/// Приоритет:
		/// - сначала ищем .git и при первом попадании возвращаем его каталог;
		/// - .sln запоминаем как кандидат, но продолжаем идти вверх;
		/// - если .git не нашли, но видели .sln — берём каталог с .sln;
		/// - иначе возвращаем исходный start.
		/// </summary>
		private static string FindRepoRootFrom ( string start )
			{
			var dir = new DirectoryInfo (start);
			DirectoryInfo? slnCandidate = null;

			while (dir != null)
				{
				var gitDir = Path.Combine (dir.FullName, ".git");
				if (Directory.Exists (gitDir))
					{
					// Нашли git-репозиторий — считаем это корнем для всех подпроектов.
					return dir.FullName;
					}

				// Если нашли .sln, запоминаем ПЕРВЫЙ встретившийся как кандидат,
				// но не выходим — продолжаем искать .git выше.
				bool hasSln = Directory.EnumerateFiles (dir.FullName, "*.sln").Any ();
				if (hasSln && slnCandidate == null)
					{
					slnCandidate = dir;
					}

				dir = dir.Parent;
				}

			// Если .git нет, но по пути виделась .sln — используем её каталог.
			if (slnCandidate != null)
				return slnCandidate.FullName;

			// Fallback — исходная директория.
			return start;
			}
		}
	}
