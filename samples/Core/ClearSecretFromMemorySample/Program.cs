using CloudMesh.Utils;

// ALWAYS consider using SecureString, when possible.
// Only ever use this approach if you're dealing with some external dependencies or situations where using SecureString
// just isn't an option. SecureString both encrypts, but also flags the memory that the secret occupies as protected, so
// that only the owning process is allowed to access it. Storing secrets as plain strings is bad practice. 
// But sometimes, you just don't have a choice.

// Allocate a string, containing a secret
var password = "MySecretPassword";

// Overwrite the memory that the string points to with blanks, then clear the string pointer.
UnsafeStringOperations.ClearMemory(ref password);

// Password is now empty string.
Console.WriteLine(password);

// Note, that clearing the memory of a string is not guaranteed to work, because GC may -move- the string around in
// memory, causing copies of it to exist.
// You can work around that, by always keeping the string pinned, while in scope.

// This should get the password from somewhere, either decrypt it from a database value, or a file, etc.
var getPassword = () => "mySecretPassword";

PinnedString.UseThenClear(getPassword, pwd =>
{
    // Authenticate
    // httpClient.PostAsync("authenticate", ...)
    Console.WriteLine(pwd);
    return true;
});

sealed unsafe class PinnedString
{
    public static TReturn UseThenClear<TReturn>(Func<string> getPassword, Func<string, TReturn> usePassword)
    {
        // Keep the password pinned in memory while we use it, so GC won't move it around.
        var password = getPassword();
        fixed (char* ptr = password)
        {
            try
            {
                // Do what you need to do with the password
                return usePassword(password);
            }
            finally
            {
                // Clear it out of memory when we're done with it.
                UnsafeStringOperations.ClearMemory(ptr, password.Length);
            }
        }
    }
}