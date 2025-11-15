using System;
using System.IO;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.Infra
	{
	/// <summary>
	/// Централизованная конфигурация путей (корень кэша вне bin).
	/// Правила:
	/// 1) env SSM_CACHE_ROOT — если задан, используем его.
	/// 2) иначе ищем корень репозитория (папка с .git или *.sln) от CWD вверх.
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

		private static string EnsureDir ( string p )
			{
			Directory.CreateDirectory (p);
			return p;
			}

		private static string FindRepoRootFrom ( string start )
			{
			var dir = new DirectoryInfo (start);
			while (dir != null)
				{
				bool hasGit = Directory.Exists (Path.Combine (dir.FullName, ".git"));
				bool hasSln = Directory.EnumerateFiles (dir.FullName, "*.sln").Any ();
				if (hasGit || hasSln) return dir.FullName;
				dir = dir.Parent;
				}
			return start;
			}
		}
	}
