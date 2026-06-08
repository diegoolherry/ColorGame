using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ColorGame.Hubs;
using ColorGame.Models;
using ColorGame.Services;
using Microsoft.AspNetCore.SignalR;
using Moq;
using Xunit;

namespace ColorGame.Tests
{
    public class TimerScenariosTests
    {
        private static (GameHub hub, Mock<IHubCallerClients> mockClients, Mock<ISingleClientProxy> mockCaller, Mock<IClientProxy> mockGroup, Mock<IHubContext<GameHub>> mockHubContext, RoomService roomService, TurnTimerService timerService) BuildHub(string connectionId = "test-connection")
        {
            var roomService = new RoomService();
            var mockHubContext = new Mock<IHubContext<GameHub>>();
            var timerService = new TurnTimerService(mockHubContext.Object, roomService);
            var hub = new GameHub(roomService, timerService);

            var mockCaller = new Mock<ISingleClientProxy>();
            mockCaller
                .Setup(c => c.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var mockGroup = new Mock<IClientProxy>();
            mockGroup
                .Setup(c => c.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var mockClients = new Mock<IHubCallerClients>();
            mockClients.Setup(c => c.Caller).Returns(mockCaller.Object);
            mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(mockGroup.Object);
            mockClients.Setup(c => c.GroupExcept(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>())).Returns(mockGroup.Object);

            var mockHubClients = new Mock<IHubClients>();
            mockHubClients.Setup(c => c.Group(It.IsAny<string>())).Returns(mockGroup.Object);
            mockHubContext.Setup(c => c.Clients).Returns(mockHubClients.Object);

            hub.Clients = mockClients.Object;

            var mockContext = new Mock<HubCallerContext>();
            mockContext.Setup(c => c.ConnectionId).Returns(connectionId);
            hub.Context = mockContext.Object;

            var mockGroups = new Mock<IGroupManager>();
            mockGroups
                .Setup(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            hub.Groups = mockGroups.Object;

            return (hub, mockClients, mockCaller, mockGroup, mockHubContext, roomService, timerService);
        }

        [Fact]
        public async Task S1_JugadorEnviaColorATiempo_CancelaTemporizador()
        {
            var (hub, _, _, _, _, roomService, timerService) = BuildHub("admin-conn");
            await hub.CreateRoom("Admin");
            var roomCode = roomService.GetRoomCodeByConnection("admin-conn");
            
            var mockContext = new Mock<HubCallerContext>();
            mockContext.Setup(c => c.ConnectionId).Returns("player-conn");
            hub.Context = mockContext.Object;
            await hub.JoinRoom(roomCode, "Player 1");

            mockContext.Setup(c => c.ConnectionId).Returns("admin-conn");
            hub.Context = mockContext.Object;
            await hub.StartGame(roomCode);

            var room = roomService.GetRoom(roomCode);
            var firstPlayerName = room.GamePlayers[0].Name;
            
            var originalTokenSource = room.Game.TurnTimerTokenSource;
            Assert.NotNull(originalTokenSource);
            Assert.False(originalTokenSource.IsCancellationRequested);

            mockContext.Setup(c => c.ConnectionId).Returns(room.GamePlayers[0].ConnectionId);
            hub.Context = mockContext.Object;
            await hub.SubmitColor(roomCode, "Rojo", 2.0);

            Assert.True(originalTokenSource.IsCancellationRequested);

            var newTokenSource = room.Game.TurnTimerTokenSource;
            Assert.NotNull(newTokenSource);
            Assert.NotEqual(originalTokenSource, newTokenSource);
            Assert.False(newTokenSource.IsCancellationRequested);
        }

        [Fact]
        public async Task S2_JugadorAgotaTiempo_EmiteGameOver()
        {
            var (hub, _, _, mockGroup, mockHubContext, roomService, timerService) = BuildHub("admin-conn");
            await hub.CreateRoom("Admin");
            var roomCode = roomService.GetRoomCodeByConnection("admin-conn");
            
            var mockContext = new Mock<HubCallerContext>();
            mockContext.Setup(c => c.ConnectionId).Returns("player-conn");
            hub.Context = mockContext.Object;
            await hub.JoinRoom(roomCode, "Player 1");

            mockContext.Setup(c => c.ConnectionId).Returns("admin-conn");
            hub.Context = mockContext.Object;
            await hub.StartGame(roomCode);

            var room = roomService.GetRoom(roomCode);
            var firstPlayerName = room.GamePlayers[0].Name;

            // wait for the 10-second timer to expire
            await Task.Delay(10500);

            // Assert Game Over was emitted by the TurnTimerService via IHubContext
            mockGroup.Verify(
                c => c.SendCoreAsync(
                    "GameOver",
                    It.Is<object[]>(args => args.Length == 4 && (string)args[0] == firstPlayerName && (string)args[1] == "Tiempo agotado"),
                    It.IsAny<CancellationToken>()),
                Times.Once);

            Assert.True(room.Game.IsOver);
        }

        [Fact]
        public async Task S3_JugadorSeDesconecta_CancelaTemporizador()
        {
            var (hub, _, _, _, _, roomService, timerService) = BuildHub("admin-conn");
            await hub.CreateRoom("Admin");
            var roomCode = roomService.GetRoomCodeByConnection("admin-conn");
            
            var mockContext = new Mock<HubCallerContext>();
            mockContext.Setup(c => c.ConnectionId).Returns("player-conn");
            hub.Context = mockContext.Object;
            await hub.JoinRoom(roomCode, "Player 1");

            mockContext.Setup(c => c.ConnectionId).Returns("admin-conn");
            hub.Context = mockContext.Object;
            await hub.StartGame(roomCode);

            var room = roomService.GetRoom(roomCode);
            var playerConnection = room.GamePlayers[1].ConnectionId;

            var originalTokenSource = room.Game.TurnTimerTokenSource;
            Assert.NotNull(originalTokenSource);

            mockContext.Setup(c => c.ConnectionId).Returns(playerConnection);
            hub.Context = mockContext.Object;
            await hub.OnDisconnectedAsync(null);

            Assert.True(originalTokenSource.IsCancellationRequested);

            // Since it was a 2 player game, and one disconnected, game ends, timer is cancelled.
            Assert.True(room.Game.IsOver);
            Assert.Null(room.Game.TurnTimerTokenSource);
        }

        [Fact]
        public async Task S4_AdministradorReinicia_CancelaTemporizador()
        {
            var (hub, _, _, _, _, roomService, timerService) = BuildHub("admin-conn");
            await hub.CreateRoom("Admin");
            var roomCode = roomService.GetRoomCodeByConnection("admin-conn");
            
            var mockContext = new Mock<HubCallerContext>();
            mockContext.Setup(c => c.ConnectionId).Returns("player-conn");
            hub.Context = mockContext.Object;
            await hub.JoinRoom(roomCode, "Player 1");

            mockContext.Setup(c => c.ConnectionId).Returns("admin-conn");
            hub.Context = mockContext.Object;
            await hub.StartGame(roomCode);

            var room = roomService.GetRoom(roomCode);

            var originalTokenSource = room.Game.TurnTimerTokenSource;
            Assert.NotNull(originalTokenSource);

            mockContext.Setup(c => c.ConnectionId).Returns("admin-conn");
            hub.Context = mockContext.Object;
            await hub.ResetGame(roomCode);

            Assert.True(originalTokenSource.IsCancellationRequested);
            Assert.Null(room.Game.TurnTimerTokenSource);
            Assert.False(room.Game.IsStarted);
        }
    }
}
