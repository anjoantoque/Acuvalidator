using DevExpress.Utils.Native;
using System.Collections.Generic;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using System.Xml;

namespace AcumaticaValidator
{
	public class Program
	{
		public const string FOLDERNAME = "AcumaticaValidator";
		public const string XMLFILENAME = "project.xml";
		public const string BINFOLDER = "Bin";

		public static string EXTRACTEDZIPPATH = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

		static void Main()
		{
			string zipPath = string.Empty;
			string binPath = string.Empty;
			//Console.WriteLine("Output File: " + EXTRACTEDZIPPATH + "\n\n");
			Console.WriteLine("Enter Package Path: ");
			zipPath = Console.ReadLine();

			bool validZip = ValidZipPath(zipPath);

			Console.WriteLine("Enter Project Bin Path: ");
			binPath = Console.ReadLine();

            Assembly.LoadFrom(Path.Combine(binPath, "PX.Data.dll"));
            Assembly.LoadFrom(Path.Combine(binPath, "PX.Objects.dll"));

            bool validBin = ValidZipPath(zipPath);


			if(validZip && validBin)
			{
				string outputPath = ExtractZIP(zipPath, binPath);

				if (!string.IsNullOrEmpty(outputPath))
				{
					ValidateProjectXML(outputPath);
				}
			}

			ResetApp();
		}

		#region Method

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
				outputPath = Path.Combine(EXTRACTEDZIPPATH, FOLDERNAME, new FileInfo(zipPath).Name.Split('.')[0]);

				if (Directory.Exists(outputPath))
				{
					Directory.Delete(outputPath, true);
				}

				ZipFile.ExtractToDirectory(zipPath, outputPath);

				//set readonly to false
				FileInfo fileInfo = new FileInfo(outputPath);
				fileInfo.IsReadOnly = false;

				//check if bin path exists for both target and package
				string outputPathBin = Path.Combine(outputPath, BINFOLDER);
				if (Directory.Exists(binPath) && Directory.Exists(outputPathBin))
				{
					var dllFiles = Directory.GetFiles(binPath, "PX.*").Where(t => Path.GetFileName(t).EndsWith("dll")).ToList();

					foreach (var file in dllFiles)
					{
						var fileName = Path.GetFileName(file);
						File.Copy(file, Path.Combine(outputPathBin, fileName), false);
					}
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.Message);
				ResetApp();
			}

			return outputPath;
		}

		protected static void DeleteExtractedZip(string path)
		{
			try
			{
				File.Delete(path);
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.Message);
				ResetApp();
			}
		}

		protected static void ValidateProjectXML(string outputZipPath)
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

				GetUsrFieldsFromDLL(outputZipPath);

				List<string> colNames = new List<string>();

				//Get usr fields in tables
				foreach (XmlNode table in tables)
				{
					foreach (XmlNode col in table.ChildNodes)
					{
						var colName = col.Attributes?.GetNamedItem("ColumnName")?.Value;

						if(!string.IsNullOrEmpty(colName))
						{
							colNames.Add(colName);
						}
					}
				}

				List<string> missingFields = new List<string>();

				//Validate USR Fields
				foreach (string usrField in usrFields)
				{
					if(!colNames.Where(t => t.ToLower() == usrField.ToLower()).Any()) 
					{
						if(!missingFields.Where(t => t.ToLower() == usrField.ToLower()).Any())
						{
							missingFields.Add(usrField);
						}
					}
				}

				if(missingFields.Any()) 
				{
					Console.WriteLine("Possible missing field: " + string.Join(", ", missingFields));
				}
				else
				{
					Console.WriteLine("Everythin looks good. Goodluck sa pag publish. HAHAHAHA");
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.Message);
				ResetApp();
			}

		}
		
		protected static List<string> GetUsrFieldsFromDLL(string outputZipPath)
		{
			List<string> fields = new List<string>();

			//get assemblies in directory.
			string folder = Path.Combine(outputZipPath, BINFOLDER);
			var files = Directory.GetFiles(folder, "*.dll").Where(t => !Path.GetFileName(t).StartsWith("PX")).ToList();
			//load each assembly.
			foreach (string file in files)
			{
				var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(file);
				//var assembly = Assembly.ReflectionOnlyLoadFrom(file);
				
				foreach (var type in assembly.GetTypes())
				{
					if (!type.IsClass) continue;
					//Console.WriteLine(type?.BaseType?.Name);
					if (!(type?.BaseType?.Name.StartsWith("PXCacheExtension")) ?? true) continue;

					foreach (var prop in type.GetProperties())
					{
						if(fields.Contains(prop.Name) || !(prop.Name.ToLower().StartsWith("usr"))) continue;

						fields.Add(prop.Name);
					}

				}
			}

			return fields;
		}


		protected static void ResetApp()
		{
			Console.WriteLine("\n");
			Main();
		}

		#endregion

	}
}