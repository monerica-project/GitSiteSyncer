using GitSiteSyncer.Models;
using LibGit2Sharp;
using System;

namespace GitSiteSyncer.Utilities
{
    public class GitHelper
    {
        private string _repoPath;
        private string _remoteName = "origin";
        private string _branch = "main";
        private GitCredentials _credentials;

        public GitHelper(string repoPath, GitCredentials credentials)
        {
            _repoPath = repoPath;
            _credentials = credentials;
        }

        public void PullAddCommitPush(string commitMessage)
        {
            try
            {
                Console.WriteLine("Pulling changes from remote, adding all files, committing, and pushing...");

                using (var repo = new Repository(_repoPath))
                {
                    var remote = repo.Network.Remotes[_remoteName];

                    // Fetch changes from the remote repository
                    Commands.Fetch(repo, remote.Name, new string[] { _branch }, new FetchOptions
                    {
                        CredentialsProvider = (url, usernameFromUrl, types) =>
                            new UsernamePasswordCredentials
                            {
                                Username = _credentials.Username,
                                Password = _credentials.Password // Use PAT for GitHub
                            }
                    }, null);

                    // Get the local and remote branches
                    var localBranch = repo.Branches[_branch];
                    var remoteBranch = repo.Branches[$"{_remoteName}/{_branch}"];

                    // Merge the fetched changes from the remote branch into the local branch
                    if (remoteBranch.Tip != localBranch.Tip)
                    {
                        var merger = new Signature(_credentials.Username, _credentials.Email, DateTime.UtcNow);
                        var mergeResult = repo.Merge(remoteBranch, merger, new MergeOptions
                        {
                            FileConflictStrategy = CheckoutFileConflictStrategy.Theirs // Choose strategy as needed
                        });

                        if (mergeResult.Status == MergeStatus.Conflicts)
                        {
                            Console.WriteLine("Conflicts occurred during merge. Resolving by keeping local changes.");
                            foreach (var conflict in repo.Index.Conflicts)
                            {
                                Console.WriteLine($"Conflict: {conflict.Ours?.Path ?? conflict.Theirs.Path}");
                                // Keep the local changes
                                if (conflict.Ours != null)
                                {
                                    repo.Index.Add(conflict.Ours.Path);
                                }
                                else if (conflict.Theirs != null)
                                {
                                    repo.Index.Remove(conflict.Theirs.Path);
                                }
                            }
                            repo.Index.Write();
                        }
                    }

                    // Stage all changes, including new, modified, and deleted files (equivalent to git add -A)
                    Commands.Stage(repo, "*");

                    // Check the status of the repository
                    var status = repo.RetrieveStatus(new StatusOptions());
                    if (status.IsDirty)
                    {
                        Console.WriteLine("Changes detected:");
                        foreach (var entry in status)
                        {
                            Console.WriteLine($"{entry.State}: {entry.FilePath}");
                        }

                        Signature author = new Signature(_credentials.Username, _credentials.Email, DateTime.UtcNow);
                        repo.Commit(commitMessage, author, author);
                    }
                    else
                    {
                        Console.WriteLine("No changes to commit.");
                    }

                    // Push commits to the remote repository
                    var options = new PushOptions
                    {
                        CredentialsProvider = (url, usernameFromUrl, types) =>
                            new UsernamePasswordCredentials()
                            {
                                Username = _credentials.Username,
                                Password = _credentials.Password // Use PAT for GitHub
                            }
                    };

                    repo.Network.Push(remote, localBranch.CanonicalName, options);
                    Console.WriteLine($"Pushed changes to {_remoteName}/{_branch}.");
                }
            }
            catch (CheckoutConflictException ex)
            {
                Console.WriteLine($"Checkout conflict error: {ex.Message}");
            }
            catch (LibGit2SharpException ex)
            {
                Console.WriteLine($"LibGit2Sharp error during Git operation: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"General error during Git operation: {ex.Message}");
            }
        }
    }
}
