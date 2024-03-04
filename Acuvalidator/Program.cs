using DevExpress.Utils.Native;
using DevExpress.Utils.Serializing;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
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
		public const string OUTPUTFOLDERNAME = "Acuvalidator";
		public const string XMLFILENAME = "project.xml";
		public const string BINFOLDERNAME = "Bin";
		public const string TEMPFOLDERNAME = "Temp";
		public const string FIELDSIGNOREFILENAME = "fields.ignore";

		public static string EXTRACTEDZIPPATH = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

		public static List<string> UNBOUNDFIELDATTRIBUTE = new List<string>()
		{
			"PXBoolAttribute",
			"PXStringAttribute",
			"PXDecimalAttribute",
			"PXLongAttribute",
			"PXIntAttribute",
			"PXDoubleAttribute",
			"PXDateAttribute",
			"PXDateAndTimeAttribute",
		};

		private static string[] _args;

		static void Main(string[] args)
		{
			_args= args;

			string zipPath = (_args.Length == 0 ? string.Empty : _args[0]);

			if(string.IsNullOrEmpty(zipPath))
			{
				Console.WriteLine("Enter Package Path: ");
				zipPath = Console.ReadLine().Trim();
			}

			AcumaticaValidator(zipPath);
		}
		static void AcumaticaValidator(string zipPath = "")
		{
			ClearTempFolder();

			bool validZip = ValidZipPath(zipPath);

			string binPath = string.Empty;

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
				binPath = Console.ReadLine().Trim();

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
				List<Fields> usrFields = new List<Fields>();

				//Get all usr fields from all classes in the package
				foreach (XmlNode graph in graphs)
				{
					string className = graph?.Attributes?.GetNamedItem("ClassName")?.Value ?? string.Empty;
					string innerText = graph.InnerText;

					Regex regex = new Regex(regExPattern);

					var result = regex.Matches(innerText);

					foreach (Match res in result)
					{
						string s = res?.Value;
						if (!string.IsNullOrEmpty(s) && !usrFields.Where(t => t.ClassName == className && t.Name == s).Any())
						{
							usrFields.Add(new Fields
							{
								ClassName = className,
								Name = s
							});
						}
					}
				}

				usrFields.AddRange(GetUsrFieldsFromDLL(outputZipPath));

				List<Fields> colNames = new List<Fields>();

				//Get usr fields in tables
				foreach (XmlNode table in tables)
				{
					foreach (XmlNode col in table.ChildNodes)
					{
						var tableName = col.Attributes?.GetNamedItem("TableName")?.Value;
						var colName = col.Attributes?.GetNamedItem("ColumnName")?.Value;

						if (!string.IsNullOrEmpty(colName))
						{
							colNames.Add(new Fields { 
								ClassName = tableName,
								Name = colName
							});
						}
					}
				}


				List<string> ignoreFields = GetIgnoreFields(zipPath);

				List<Fields> missingFields = new List<Fields>();

				//Validate USR Fields
				foreach (Fields usrField in usrFields)
				{
					//if field is an extension then not neccessary to match the classname in the tablename
					if (!colNames.Where(t => (string.IsNullOrEmpty(t.ExtensionClassName) || t.ClassName == usrField.ClassName) && t.Name.ToLower() == usrField.Name.ToLower()).Any())
					{
						//skip field if listed in ignore fields
						if (ignoreFields.Where(t => t.ToLower() == usrField.Name.ToLower()).Any()) continue;

						if (!missingFields.Where(t => t.ClassName == usrField.ClassName && t.Name.ToLower() == usrField.Name.ToLower()).Any())
						{
							missingFields.Add(usrField);
						}
					}
				}

				if (missingFields.Any())
				{
					List<string> missing = new List<string>();
					
					missingFields.ForEach(t =>
					{
						//missing.Add((!string.IsNullOrEmpty(t.ExtensionClassName) ? t.ExtensionClassName : t.ClassName) + "." + t.Name);
						missing.Add(t.ClassName + "." + t.Name);
					});
					
					string errMsg = "\nWarning: Possible missing field:\n" + string.Join("\n", missing);
					Console.ForegroundColor = ConsoleColor.Red;
					Console.WriteLine(errMsg);
					Console.ResetColor();

					//throw new Exception(errMsg);
				}
				else
				{
					Console.ForegroundColor = ConsoleColor.Green;
					Console.WriteLine("Everything looks good. Goodluck sa pag publish. HAHAHAHA");
					Console.ResetColor();
				}

				DeleteDirectory(outputZipPath);

			}
			catch (Exception ex)
			{
				//throw new Exception(ex.Message);
				Console.WriteLine(ex.Message);
				ResetApp();
			}
		}

		protected static List<Fields> GetUsrFieldsFromDLL(string outputZipPath)
		{
			List<Fields> fields = new List<Fields>();

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

					//TODO: Get the fields here from custom DAC's
					#region Get the fields from custom DAC's

					#endregion

					if (!(type?.BaseType?.Name.StartsWith("PXCacheExtension")) ?? true) continue;

					#region Get Fields From Extension

					foreach (var prop in type.GetProperties())
					{
						if (fields.Where(t => t.ClassName == type.Name && t.Name == prop.Name).Any() || !(prop.Name.ToLower().StartsWith("usr"))) continue;

						bool isUnboundField = false;

						foreach (var attr in prop.CustomAttributes)
						{
							string attrName = attr?.AttributeType?.Name;

							if (UNBOUNDFIELDATTRIBUTE.Contains(attrName))
							{
								isUnboundField = true;
								break;
							}
						}

						//skip fields with unbound attribute
						if (isUnboundField) continue;

						fields.Add(new Fields
						{
							ExtensionClassName = type.Name,
							ClassName = type.BaseType?.GetGenericArguments()?.FirstOrDefault()?.Name,
							Name = prop.Name,
						});
					}

					#endregion
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
				//Console.WriteLine(ex.Message);
				//ResetApp();
			}
		}

		protected static void ResetApp()
		{
			Console.WriteLine("\n");

			if(_args.Length == 2)
			{
				if (_args[1]?.ToLower() == "true")
				{
					Environment.Exit(0);
				}
			}

			Main(new string[] { });
		}

		protected static void ClearTempFolder()
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

			if (dir != null)
			{
				var filleExists = File.Exists(Path.Combine(dir.FullName, FIELDSIGNOREFILENAME));

				if (filleExists)
				{
					var ignoreFields = File.ReadAllLines(Path.Combine(dir.FullName, FIELDSIGNOREFILENAME));

					foreach (var field in ignoreFields)
					{
						if (fields.Contains(field)) continue;

						fields.Add(field.ToLower().Trim());
					}
				}
				else
				{
					string errMsg = "\nWarning: Ignore file was not found";
					Console.ForegroundColor = ConsoleColor.Yellow;
					Console.WriteLine(errMsg);
					Console.ResetColor();
				}

			}

			return fields;
		}
		#endregion

	}

	public class Fields
	{
		public string? ExtensionClassName { get; set; }
		public string? ClassName { get; set; }
		public string? Name { get; set; }
		public string? Type { get; set; }
	}
}