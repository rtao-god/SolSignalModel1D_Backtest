namespace SolSignalModel1D_Backtest.Core.Infra
	{
	/// <summary>
	/// Централизованная конфигурация путей.
	/// Правила:
	/// 1) env SSM_CACHE_ROOT — если задан, используем его как корень для cache/.
	/// 2) иначе ищем корень репозитория (папка с .git или *.sln) от CWD вверх.
	/// 3) fallback: текущая директория.
	///
	/// Дополнительно:
	/// - RepoRoot — родитель cache/ (если SSM_CACHE_ROOT задан, то он и есть база).
	/// - GetRepoSubdir(...) — универсальный способ получить <RepoRoot>/<подпуть>.
	/// </summary>
	public static class PathConfig
		{
		private static readonly Lazy<string> _cacheRoot = new Lazy<string> (() =>
		{
			var env = Environment.GetEnvironmentVariable ("SSM_CACHE_ROOT");
			if (!string.IsNullOrWhiteSpace (env))
				return EnsureDir (env);

			var repo = FindRepoRootFrom (Directory.GetCurrentDirectory ());
			var root = Path.Combine (repo, "cache");
			return EnsureDir (root);
		});

		/// <summary>
		/// Корень кэша:
		///   - или SSM_CACHE_ROOT;
		///   - или <RepoRoot>/cache.
		/// </summary>
		public static string CacheRoot => _cacheRoot.Value;

		/// <summary>
		/// Логический "корень репозитория" для служебных папок.
		/// Обычно это родитель cache/:
		///   <RepoRoot>/
		///     cache/
		///     profiles/
		///     reports/
		///     ...
		/// </summary>
		public static string RepoRoot
			{
			get
				{
				var cacheDirInfo = new DirectoryInfo (CacheRoot);
				return cacheDirInfo.Parent?.FullName ?? CacheRoot;
				}
			}

		public static string CandlesDir => EnsureDir (Path.Combine (CacheRoot, "candles"));
		public static string IndicatorsDir => EnsureDir (Path.Combine (CacheRoot, "indicators"));

		/// <summary>
		/// Каталог профилей бэктеста.
		/// Использует общий механизм GetRepoSubdir.
		/// </summary>
		public static string ProfilesDir => GetRepoSubdir ("profiles");

		/// <summary>
		/// Универсальный способ получить подкаталог относительно RepoRoot:
		/// GetRepoSubdir("reports") → <RepoRoot>/reports
		/// GetRepoSubdir("reports", "pfi") → <RepoRoot>/reports/pfi
		/// </summary>
		public static string GetRepoSubdir ( params string[] segments )
			{
			if (segments == null || segments.Length == 0)
				throw new ArgumentException ("segments must be non-empty.", nameof (segments));

			var nonEmpty = segments
				.Where (s => !string.IsNullOrWhiteSpace (s))
				.ToArray ();

			if (nonEmpty.Length == 0)
				throw new ArgumentException ("segments must contain at least one non-empty.", nameof (segments));

			var path = nonEmpty.Aggregate (RepoRoot, Path.Combine);
			return EnsureDir (path);
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
					return dir.FullName;
					}

				bool hasSln = Directory.EnumerateFiles (dir.FullName, "*.sln").Any ();
				if (hasSln && slnCandidate == null)
					{
					slnCandidate = dir;
					}

				dir = dir.Parent;
				}

			if (slnCandidate != null)
				return slnCandidate.FullName;

			return start;
			}
		}
	}
