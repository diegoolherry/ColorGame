using System.Threading;
using ColorGame.Models;
using Xunit;

namespace ColorGame.Tests.Models
{
    public class GameStateTests
    {
        [Fact]
        public void GameState_ShouldHaveTurnTimerTokenSourceProperty()
        {
            var gameState = new GameState();
            
            // Verifying the property exists and can be set to a CancellationTokenSource
            CancellationTokenSource cts = new CancellationTokenSource();
            gameState.TurnTimerTokenSource = cts;
            
            Assert.Same(cts, gameState.TurnTimerTokenSource);
        }
    }
}
