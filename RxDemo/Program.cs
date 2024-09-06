using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;

public class Token
{
    private static int _counter = 0;
    public Token()
    {
        Value = $"Token-{++_counter}";
    }
    public string Value { get; }
    public DateTime ValidTo { get; } = DateTime.Now.AddSeconds(5);
}

public static class UserAuthentication
{
    private static int _counter = 0;
    public static Token UserToken { get; private set; } = new Token();

    public static event EventHandler OnSignInStatusUpdated;

    public static async Task UpdateSignInStatus(CancellationToken ct)
    {
        await Task.Delay(1000, ct);
        UserToken = new Token();
        OnSignInStatusUpdated?.Invoke(null, EventArgs.Empty);
    }
}

public class TokenService
{
    
    private IObservable<Token> RequestTokenUpdate()
    {

        return Observable.Create<Token>(async observer =>
        {
            var cts = new CancellationTokenSource();
            //var token = new Token();
            observer.OnNext(UserAuthentication.UserToken);

            void OnUserAuthenticationOnOnSignInStatusUpdated(object? sender, EventArgs args)
            {
                observer.OnNext(UserAuthentication.UserToken);
                observer.OnCompleted();
            }

            UserAuthentication.OnSignInStatusUpdated += OnUserAuthenticationOnOnSignInStatusUpdated;

            await UserAuthentication.UpdateSignInStatus(cts.Token);
            //observer.OnCompleted();
            return () =>
            {
                UserAuthentication.OnSignInStatusUpdated -= OnUserAuthenticationOnOnSignInStatusUpdated;
                Console.WriteLine($"No longer requesting a token");
                cts.Cancel();
            };
        });
    }

    // Simulate fetching a new token
    public IObservable<Token> FetchToken()
    {
        var tokenStream = Observable.Return(UserAuthentication.UserToken).Select(token =>
        {
            return Observable.Return(token)
                .Concat(Observable.Timer(token.ValidTo - DateTimeOffset.UtcNow)
                    .Select((_) => RequestTokenUpdate()).Switch()
                );
        }).Switch();
        return tokenStream;
    }

    public IObservable<Token> GetTokenStream()
    {
        return Observable.Defer(() =>
            FetchToken()
        ).Repeat();
    }
}

class Program
{
    static void Main()
    {
        var tokenService = new TokenService();
        var tokenStream = tokenService.GetTokenStream();//.Multicast(new ReplaySubject<Token>(1)).RefCount();

        
        var subscriptions = Array.Empty<(int, IDisposable)>();
        // Keep the application running to observe the token stream
        Console.WriteLine("Press W to subscribe, press S to unsubscribe...");
        var count = 0;
        while (true)
        {
            var key = Console.ReadKey();
            if (key.KeyChar == 'w')
            {
                var id = count++;
                subscriptions = subscriptions.Append((id, Subscribe(tokenStream, id))).ToArray();
            }
            else if(key.KeyChar == 's')

            {
                if (subscriptions.Length > 0)
                {
                    var (subId, subscription) = subscriptions[0];
                    Console.WriteLine($"Removing subscription {subId}");
                    subscription.Dispose();
                    subscriptions = subscriptions.Skip(1).ToArray();
                }
                else
                {
                    Console.WriteLine("No subscriptions to remove");
                }
            }
            else
            {
                break;
            }
        }
        

        // Dispose the subscription when done
        foreach (var valueTuple in subscriptions)
        {
            valueTuple.Item2.Dispose();
        };
    }

    private static IDisposable Subscribe(IObservable<Token> tokenStream, int subId)
    {
        var subscription = tokenStream.Subscribe(
            token => Console.WriteLine($"New token received by {subId}, with value {token.Value} valid until: {token.ValidTo}"),
            error => Console.WriteLine($"Error: {error}"),
            () => Console.WriteLine("Completed")
        );
        return subscription;
    }
}