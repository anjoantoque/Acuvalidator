using DevExpress.Utils.Native;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using System.Xml;

namespace AcumaticaValidator
{
	public class Program
	{
		public const string APPLICATIONFOLDERNAME = "Application";
		public const string OUTPUTFOLDERNAME = "AcumaticaValidator";
		public const string XMLFILENAME = "project.xml";
		public const string BINFOLDERNAME = "Bin";
		public const string TEMPFOLDERNAME = "Temp";
		public const string FIELDSIGNOREFILENAME = "fields.ignore";

		public static string EXTRACTEDZIPPATH = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
		static void Main()
		{
			Main(false);
		}
		static void Main(bool reset)
		{
			if(!reset)
			{
				ClearTemFolder();
			}

			string zipPath = string.Empty;
			string binPath = string.Empty;

			Console.WriteLine("Enter Package Path: ");
			zipPath = Console.ReadLine();
			bool validZip = ValidZipPath(zipPath);

			//get the bin path from the supplied zip path

			if (validZip) 
			{
				var directoryPath = zipPath.Split("\\");
				
				foreach (var dir in directoryPath)
				{
					binPath = Path.Combine(binPath, dir);

					if (dir.ToLower() == APPLICATIONFOLDERNAME.ToLower())
					{
						//if the application folder is found then navigate to the bin folder
						binPath = Path.Combine(binPath, BINFOLDERNAME);
						break;
					}
				}
			}

			bool validBin = ValidPath(binPath);

			//if the bin path generate from above is invalid then force user to manually input
			if (!validBin)
			{
				Console.WriteLine("\nFailed to locate Project Bin Path");
				Console.WriteLine("Enter Project Bin Path: ");
				binPath = Console.ReadLine();

				validBin = ValidPath(binPath);
			}

			if (validZip && validBin)
			{
				#region Load Acumatica Dependencies DLL
				Assembly.LoadFrom(Path.Combine(binPath, "PX.Data.dll"));
				Assembly.LoadFrom(Path.Combine(binPath, "PX.Objects.dll"));
				#endregion

				string outputPath = ExtractZIP(zipPath, binPath);

				if (!string.IsNullOrEmpty(outputPath))
				{
					ValidateProjectXML(zipPath, outputPath);
				}
			}
			else
			{
				Console.WriteLine("\nError: Failed to locate Project Bin or Package Path");
			}

			ResetApp();
		}

		#region Method
		protected static void ValidateProjectXML(string zipPath, string outputZipPath)
		{
			try
			{
				XmlDocument doc = new XmlDocument();
				doc.Load(Path.Combine(outputZipPath, XMLFILENAME));

				List<XmlNode> graphs = new List<XmlNode>();
				List<XmlNode> tables = new List<XmlNode>();

				foreach (XmlNode node in doc?.DocumentElement?.ChildNodes)
				{
					switch (node.Name)
					{
						case "Graph":
							graphs.Add(node);
							break;
						case "Table":
							tables.Add(node);
							break;
						default:
							break;
					}
				}

				string regExPattern = @"usr\w+";
				List<string> usrFields = new List<string>();

				//Get all usr fields from all classes in the package
				foreach (XmlNode graph in graphs)
				{
					string innerText = graph.InnerText;

					Regex regex = new Regex(regExPattern);

					var result = regex.Matches(innerText);

					foreach (Match res in result)
					{
						string s = res?.Value;
						if (!string.IsNullOrEmpty(s) && !usrFields.Contains(s))
						{
							usrFields.Add(s);
						}
					}
				}

				usrFields.AddRange(GetUsrFieldsFromDLL(outputZipPath));

				List<string> colNames = new List<string>();

				//Get usr fields in tables
				foreach (XmlNode table in tables)
				{
					foreach (XmlNode col in table.ChildNodes)
					{
						var colName = col.Attributes?.GetNamedItem("ColumnName")?.Value;

						if (!string.IsNullOrEmpty(colName))
						{
							colNames.Add(colName);
						}
					}
				}


				List<string> ignoreFields = GetIgnoreFields(zipPath);

				List<string> missingFields = new List<string>();

				//Validate USR Fields
				foreach (string usrField in usrFields)
				{
					if (!colNames.Where(t => t.ToLower() == usrField.ToLower()).Any())
					{
						//skip field if listed in ignore fields
						if (ignoreFields.Where(t => t.ToLower() ==usrField.ToLower()).Any()) continue;

						if (!missingFields.Where(t => t.ToLower() == usrField.ToLower()).Any())
						{
							missingFields.Add(usrField);
						}
					}
				}

				if (missingFields.Any())
				{
					string errMsg = "\nWarning: Possible missing field:\n" + string.Join("\n", missingFields);
					Console.ForegroundColor = ConsoleColor.Red;
					Console.WriteLine(errMsg);
					Console.ResetColor();

					throw new Exception(errMsg);
				}
				else
				{
					Console.ForegroundColor = ConsoleColor.Green;
					Console.WriteLine("Everythin looks good. Goodluck sa pag publish. HAHAHAHA");
					Console.ResetColor();
				}

				DeleteDirectory(outputZipPath);

			}
			catch (Exception ex)
			{
				throw new Exception(ex.Message);
			}
		}

		protected static List<string> GetUsrFieldsFromDLL(string outputZipPath)
		{
			List<string> fields = new List<string>();

			//get assemblies in directory.
			string folder = CopyFilesToTempFolder(Path.Combine(outputZipPath, BINFOLDERNAME));
			var files = Directory.GetFiles(folder, "*.dll").Where(t => !Path.GetFileName(t).StartsWith("PX")).ToList();
			//load each assembly.
			foreach (string file in files)
			{
				var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(file);

				foreach (var type in assembly.GetTypes())
				{
					if (!type.IsClass) continue;
					//Console.WriteLine(type?.BaseType?.Name);
					if (!(type?.BaseType?.Name.StartsWith("PXCacheExtension")) ?? true) continue;

					foreach (var prop in type.GetProperties())
					{
						if (fields.Contains(prop.Name) || !(prop.Name.ToLower().StartsWith("usr"))) continue;

						fields.Add(prop.Name);
					}
				}
			}

			return fields;
		}
		#endregion

		#region Helpers
		protected static bool ValidPath(string path)
		{
			bool result = false;
			try
			{
				result = Directory.Exists(path);
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.Message);
				ResetApp();
			}

			return result;
		}

		protected static bool ValidZipPath(string path)
		{
			bool result = false;
			try
			{
				using (var zipFile = ZipFile.OpenRead(path))
				{
					var entries = zipFile.Entries;
					result = true;
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.Message);
				ResetApp();
			}

			return result;
		}

		protected static string ExtractZIP(string zipPath, string binPath)
		{
			string outputPath = string.Empty;

			try
			{
				outputPath = Path.Combine(EXTRACTEDZIPPATH, OUTPUTFOLDERNAME, new FileInfo(zipPath).Name.Split('.')[0]);

				if (Directory.Exists(outputPath))
				{
					DeleteDirectory(outputPath);
				}

				ZipFile.ExtractToDirectory(zipPath, outputPath);

				//set readonly to false
				FileInfo fileInfo = new FileInfo(outputPath);
				fileInfo.IsReadOnly = false;

				//check if bin path exists for both target and package
				//string outputPathBin = Path.Combine(outputPath, BINFOLDERNAME);
				//if (Directory.Exists(binPath) && Directory.Exists(outputPathBin))
				//{
				//	var dllFiles = Directory.GetFiles(binPath, "PX.*").Where(t => Path.GetFileName(t).EndsWith("dll")).ToList();

				//	foreach (var file in dllFiles)
				//	{
				//		var fileName = Path.GetFileName(file);
				//		File.Copy(file, Path.Combine(outputPathBin, fileName), false);
				//	}
				//}
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.Message);
				ResetApp();
			}

			return outputPath;
		}

		protected static void DeleteDirectory(string path)
		{
			try
			{
				if (!Directory.Exists(path)) return;

				ClearReadOnly(path);

				Directory.Delete(path, true);
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.Message);
				ResetApp();
			}
		}

		protected static void ResetApp()
		{
			Console.WriteLine("\n");

			Main(true);
		}

		protected static void ClearTemFolder()
		{
			DeleteDirectory(Path.Combine(EXTRACTEDZIPPATH, OUTPUTFOLDERNAME, TEMPFOLDERNAME));
		}

		protected static void ClearReadOnly(string path)
		{
			var parentDirectory = new DirectoryInfo(path);

			if (parentDirectory != null)
			{
				parentDirectory.Attributes = FileAttributes.Normal;
				foreach (FileInfo fi in parentDirectory.GetFiles())
				{
					fi.Attributes = FileAttributes.Normal;
				}
				foreach (DirectoryInfo di in parentDirectory.GetDirectories())
				{
					ClearReadOnly(di.FullName);
				}
			}
		}

		private static void CopyFilesRecursively(string sourcePath, string targetPath)
		{
			//Now Create all of the directories
			foreach (string dirPath in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
			{
				Directory.CreateDirectory(dirPath.Replace(sourcePath, targetPath));
			}

			//Copy all the files & Replaces any files with the same name
			foreach (string newPath in Directory.GetFiles(sourcePath, "*.*", SearchOption.AllDirectories))
			{
				File.Copy(newPath, newPath.Replace(sourcePath, targetPath), true);
			}
		}

		protected static string CopyFilesToTempFolder(string sourcePath)
		{
			string outputPath = Path.Combine(EXTRACTEDZIPPATH, OUTPUTFOLDERNAME, TEMPFOLDERNAME, Guid.NewGuid().ToString());

			try
			{
				if (!Directory.Exists(outputPath))
				{
					Directory.CreateDirectory(outputPath);
				}

				CopyFilesRecursively(sourcePath, outputPath);
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.Message);
				ResetApp();
			}

			return outputPath;
		}

		protected static List<string> GetIgnoreFields(string zipPath)
		{
			List<string> fields = new List<string>();

			var dir = Directory.GetParent(zipPath);

			if(dir != null)
			{
				var ignoreFields = File.ReadAllLines(Path.Combine(dir.FullName, FIELDSIGNOREFILENAME));

				foreach (var field in ignoreFields)
				{
					if (fields.Contains(field)) continue;

					fields.Add(field.ToLower().Trim());
				}
			}	

			return fields;
		}
		#endregion

	}
}