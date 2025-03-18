using NuGet.Protocol.Core.Types;
using NuGet.Protocol;
using NuGet.Configuration;
using NuGet.Common;
using System.Xml.Linq;
using LibGit2Sharp;
using Repository = NuGet.Protocol.Core.Types.Repository;
using System.Linq;

class Program
{
    static async Task Main(string[] args)
    {
        // Prompt the user for input if arguments are not provided
        var repoPath = args.Length > 1 ? args[1] : PromptUser("Enter the local repository path: ");
        var username = args.Length > 2 ? args[2] : PromptUser("Enter User Name: ");
        var password = args.Length > 3 ? args[3] : PromptUser("Enter the PAT/Password: ");
        var branchName = args.Length > 4 ? args[4] : PromptUser("Enter Branch Name: ");
        var feedUrl = args.Length > 5 ? args[5] : PromptUser("Enter Feed URL Name: ");
       

        // Create a NuGet SourceRepository with credentials
        var packageSource = new PackageSource(feedUrl)
        {
          Credentials = new PackageSourceCredential("AzureArtifacts", "username", password,
            isPasswordClearText: true, validAuthenticationTypesText: null)
        };

        var sourceRepository = Repository.Factory.GetCoreV3(packageSource);

        var IsNeedToPush = false;
        List<string> csprojFiles = GetCsprojFiles(repoPath);


        // Find and update packages
        try
        {
          FetchBranch(repoPath, branchName, username, password);
          PullBranch(repoPath, branchName, username, password);

          foreach (var csprojFilePath in csprojFiles)
          {
            Console.WriteLine($"\n'{csprojFilePath}'");


            // List of CivilGeo.* packages to update
            var packagereferancelist = GetPackageReferences(csprojFilePath);
               
            Console.WriteLine($"List of CivilGEO Packages referred in project file");
            foreach (var packagename in packagereferancelist)
            {
              Console.WriteLine($"'{packagename}'");
            } 
            Console.WriteLine($"****************************************************************************************************");
            foreach (var packageToUpdate in packagereferancelist)
            {
             
                var latestVersion = await GetLatestVersionAsync(sourceRepository, packageToUpdate);
                var filePackageVersion = GetPackageVersion(csprojFilePath, packageToUpdate);
                if (!latestVersion.Equals(filePackageVersion, StringComparison.InvariantCultureIgnoreCase))
                {
                  UpdatePackageVersion(csprojFilePath, packageToUpdate, latestVersion);
                  Console.WriteLine(
                    $"package '{packageToUpdate}' is updated from '{filePackageVersion}' to latest version '{latestVersion}'");
                    IsNeedToPush = true;
                }
                else
                {
                  Console.WriteLine($"package '{packageToUpdate}' already updated.");
                }
            }
          }

        }
        catch (Exception ex)
        {
          Console.WriteLine($"Error: {ex.Message}");
        }
    }

    public static List<string> GetCsprojFiles(string repoPath)
    {
        if (string.IsNullOrWhiteSpace(repoPath))
        {
            throw new ArgumentException("The rootPath cannot be null or empty.", nameof(repoPath));
        }

        if (!Directory.Exists(repoPath))
        {
            throw new DirectoryNotFoundException($"The directory '{repoPath}' does not exist.");
        }

        List<string> projectFiles = new List<string>();

        try
        {
            // Get all .csproj files in the directory and subdirectories.
            projectFiles.AddRange(Directory.GetFiles(repoPath, "*.csproj", SearchOption.AllDirectories));
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.WriteLine($"Access denied to a directory: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred while searching for project files: {ex.Message}");
        }

        return projectFiles;
    }


public static List<string> GetPackageReferences(string csprojFilePath)
    {
        if (string.IsNullOrEmpty(csprojFilePath) || !File.Exists(csprojFilePath))
        {
            throw new FileNotFoundException("The specified .csproj file does not exist.");
        }

        var civilGeoPackages = new List<string>();

        try
        {
            XDocument csprojXml = XDocument.Load(csprojFilePath);
            var packageReferences = csprojXml.Descendants("PackageReference")
                .Where(pr => pr.Attribute("Include") != null &&
                             pr.Attribute("Include").Value.StartsWith("CivilGeo", StringComparison.OrdinalIgnoreCase));

            foreach (var packageReference in packageReferences)
            {
                civilGeoPackages.Add(packageReference.Attribute("Include").Value);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred while reading the .csproj file: {ex.Message}");
        }

        return civilGeoPackages;
    }

    private static string PromptUser(string message)
    {
        Console.Write(message);
        return Console.ReadLine() ?? string.Empty;
    }

  public static async Task<string> GetLatestVersionAsync(SourceRepository sourceRepository, string packageName)
  {
    var retryCount = 3;
    for (int attempt = 0; attempt < retryCount; attempt++)
    {
      try
      {
        var searchResource = await sourceRepository.GetResourceAsync<PackageSearchResource>();
        var searchResults = await searchResource.SearchAsync(
          packageName,
          new SearchFilter(includePrerelease: true),
          0, // Start index
          1, // Get only the top result
          NullLogger.Instance,
          CancellationToken.None);

        var package = searchResults.First();
        if (package == null)
        {
          throw new Exception($"Package '{packageName}' not found.");
        }

        return package.Identity.Version.ToString();
      }
      catch (Exception ex) when (attempt < retryCount - 1)
      {
        await Task.Delay(2000); // Wait before retrying
      }
    }
    throw new Exception("Failed to retrieve the latest version after multiple attempts.");
  }

  public static string GetPackageVersion(string csprojFilePath, string packageName)
    {
        if (!File.Exists(csprojFilePath))
            throw new FileNotFoundException($"The file '{csprojFilePath}' does not exist.");

        var doc = XDocument.Load(csprojFilePath);
        var packageElement = GetPackageElement(doc, packageName);

        return packageElement.Attribute("Version").Value;
    }

    public static void UpdatePackageVersion(string csprojFilePath, string packageName, string? newVersion)
    {
        if (!File.Exists(csprojFilePath))
            throw new FileNotFoundException($"The file '{csprojFilePath}' does not exist.");

        var doc = XDocument.Load(csprojFilePath);
        var packageElement = GetPackageElement(doc, packageName);

        if (packageElement == null) 
            return;

        packageElement.SetAttributeValue("Version", newVersion);
        doc.Save(csprojFilePath);
    }

    private static XElement GetPackageElement(XDocument doc, string packageName)
    {
        return doc.Descendants("PackageReference")
            .First(pr => pr.Attribute("Include")?.Value == packageName);
    }

    static void FetchBranch(string repoPath, string branchName, string username, string password)
    {
        using var repo = new LibGit2Sharp.Repository(repoPath);
        Console.WriteLine($"\nFetching '{branchName}' branch...\n\n");
        var remote = repo.Network.Remotes["origin"];
        Commands.Fetch(repo, remote.Name, new[] { $"refs/heads/{branchName}:refs/remotes/origin/{branchName}" },
            new FetchOptions
            {
                CredentialsProvider = (url, user, cred) => new UsernamePasswordCredentials
                {
                    Username = username,
                    Password = password
                }
            },
            null);
    }

    static void PullBranch(string repoPath, string branchName, string username, string password)
    {
        using var repo = new LibGit2Sharp.Repository(repoPath);
        Console.WriteLine($"Pulling '{branchName}' branch...\nCurrent branch '{branchName}' \nRepo Path '{repoPath}'\n\n");
        var signature = new Signature("Mayank Jaiswal", "jaiswal.mayank@chrismaedercmwatergroup.onmicrosoft.com", DateTimeOffset.Now);

        // Ensure you're on the correct branch
        var localBranch = repo.Branches[branchName];
        if (localBranch == null)
        {
            throw new Exception($"Branch '{branchName}' does not exist locally.");
        }
        Commands.Checkout(repo, localBranch);

        // Pull changes
        Commands.Pull(repo, signature, new PullOptions
        {
            FetchOptions = new FetchOptions
            {
                CredentialsProvider = (url, user, cred) => new UsernamePasswordCredentials
                {
                    Username = username,
                    Password = password
                }
            }
        });
    }

  
}
