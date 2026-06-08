using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ColorGame.Hubs;
using ColorGame.Services;
using Microsoft.AspNetCore.SignalR;
using Moq;
using Xunit;

namespace ColorGame.Tests
{
    public class GameHubTests
    {
        private static (GameHub hub, Mock<IHubCallerClients> mockClients, Mock<ISingleClientProxy> mockCaller) BuildHub()
        {
            var roomService = new RoomService();
            var mockHubContext = new Mock<IHubContext<GameHub>>();
            var timerService = new TurnTimerService(mockHubContext.Object, roomService);
            var hub = new GameHub(roomService, timerService);

            var mockCaller = new Mock<ISingleClientProxy>();
            mockCaller
                .Setup(c => c.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var mockClients = new Mock<IHubCallerClients>();
            mockClients.Setup(c => c.Caller).Returns(mockCaller.Object);

            hub.Clients = mockClients.Object;

            var mockContext = new Mock<HubCallerContext>();
            mockContext.Setup(c => c.ConnectionId).Returns("test-connection");
            hub.Context = mockContext.Object;

            var mockGroups = new Mock<IGroupManager>();
            mockGroups
                .Setup(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            hub.Groups = mockGroups.Object;

            return (hub, mockClients, mockCaller);
        }

        // T3: GameHub.CreateRoom receives category and emits RoomCreated(code, category)
        [Fact]
        public async Task GameHub_CreateRoom_EmitsRoomCreatedWithCategory()
        {
            var (hub, _, mockCaller) = BuildHub();

            await hub.CreateRoom("Admin", "Países");

            mockCaller.Verify(
                c => c.SendCoreAsync(
                    "RoomCreated",
                    It.Is<object[]>(args => args.Length == 2 && (string)args[1] == "Países"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task GameHub_CreateRoom_DefaultCategory_EmitsColores()
        {
            var (hub, _, mockCaller) = BuildHub();

            await hub.CreateRoom("Admin");

            mockCaller.Verify(
                c => c.SendCoreAsync(
                    "RoomCreated",
                    It.Is<object[]>(args => args.Length == 2 && (string)args[1] == "Colores"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        // T4: GameHub.JoinRoom emits JoinedRoom(code, players, category)
        [Fact]
        public async Task GameHub_JoinRoom_EmitsJoinedRoomWithCategory()
        {
            var roomService = new RoomService();
            // Create a room with category "Animales"
            roomService.CreateRoom("Admin", "conn-admin", "Animales");
            // get the created room code
            var adminRoom = roomService.GetRoomCodeByConnection("conn-admin");

            var mockHubContext = new Mock<IHubContext<GameHub>>();
            var timerService = new TurnTimerService(mockHubContext.Object, roomService);
            var hub = new GameHub(roomService, timerService);

            var mockCaller = new Mock<ISingleClientProxy>();
            mockCaller
                .Setup(c => c.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var mockGroupProxy = new Mock<ISingleClientProxy>();
            mockGroupProxy
                .Setup(c => c.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var mockClients = new Mock<IHubCallerClients>();
            mockClients.Setup(c => c.Caller).Returns(mockCaller.Object);
            mockClients.Setup(c => c.GroupExcept(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>())).Returns(mockGroupProxy.Object);

            hub.Clients = mockClients.Object;

            var mockContext = new Mock<HubCallerContext>();
            mockContext.Setup(c => c.ConnectionId).Returns("conn-player");
            hub.Context = mockContext.Object;

            var mockGroups = new Mock<IGroupManager>();
            mockGroups
                .Setup(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            hub.Groups = mockGroups.Object;

            await hub.JoinRoom(adminRoom!, "Player1");

            mockCaller.Verify(
                c => c.SendCoreAsync(
                    "JoinedRoom",
                    It.Is<object[]>(args => args.Length == 3 && (string)args[2] == "Animales"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }
}
