
using LibGit2Sharp;

class Push_Changes
{
    static async Task Main(string[] args)
    {
        // Prompt the user for input if arguments are not provided
        var repoPath = args.Length > 1 ? args[1] : PromptUser("Enter the local repository path: ");
        var password = args.Length > 2 ? args[2] : PromptUser("Enter the PAT/Password: ");
        var branchName = args.Length > 3 ? args[3] : PromptUser("Enter Branch Name: ");
        var username =  args.Length > 4 ? args[4] : PromptUser("Enter User Credencial ID: ");

        if (string.IsNullOrEmpty(branchName))
        {
          branchName = "master";
        }

       
        // Find and update packages
        try
        {
          FetchBranch(repoPath, branchName, username, password);
          PullBranch(repoPath, branchName, username, password);
          CommitChanges(repoPath, "Updated package reference in .csproj file", "Author Name",username);
          PushBranch(repoPath, branchName, username, password);
        }
        catch (Exception ex)
        {
          Console.WriteLine($"Error: {ex.Message}");
        }
    }


    private static string PromptUser(string message)
    {
        Console.Write(message);
        return Console.ReadLine() ?? string.Empty;
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
        var signature = new Signature("Author Name", username, DateTimeOffset.Now);

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

    static void CommitChanges(string repoPath, string message, string authorName, string authorEmail)
    {
        using var repo = new LibGit2Sharp.Repository(repoPath);
        Console.WriteLine("Staging and committing .csproj file changes...");

        // Filter and stage only .csproj files
        bool anyFilesStaged = false; // Flag to check if any files were staged
        foreach (var file in repo.RetrieveStatus().Where(status => status.FilePath.EndsWith(".csproj") || status.FilePath.EndsWith(".props")))
        {
            Console.WriteLine($"Staging file: {file.FilePath}");
            Commands.Stage(repo, file.FilePath);
            anyFilesStaged = true;
        }

        // Check if there are staged changes
        if (!repo.Index.Any())
        {
            Console.WriteLine("No .csproj or .props changes to commit.");
            return;
        }
        if (!anyFilesStaged)
        {
            Console.WriteLine("No .csproj or .props changes to commit.");
            return; // Exit the method gracefully
        }

        // Create the commit
        var author = new Signature(authorName, authorEmail, DateTimeOffset.Now);
        var committer = author; // You can use a different committer if necessary
        repo.Commit(message, author, committer);

        Console.WriteLine("Changes committed successfully.");
    }


    static void PushBranch(string repoPath, string branchName, string username, string password)
    {
        using var repo = new LibGit2Sharp.Repository(repoPath);
        Console.WriteLine("\nPushing branch...");
        var branch = repo.Branches[branchName];
        Console.WriteLine($"BRANCH NAME TO PUSH '{repo.Branches[branchName]}'...");
        if (branch == null)
        {
            throw new Exception($"Branch '{branchName}' does not exist locally.");
        }

        repo.Network.Push(branch, new PushOptions
        {
            CredentialsProvider = (url, user, cred) => new UsernamePasswordCredentials
            {
                Username = username,
                Password = password
            }
        });

        Console.WriteLine("Branch pushed successfully.");
    }
}
