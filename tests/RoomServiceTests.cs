using ColorGame.Services;
using Xunit;

namespace ColorGame.Tests
{
    public class RoomServiceTests
    {
        // T2: CreateRoom accepts category and assigns it to the room
        [Fact]
        public void CreateRoom_WithCategory_AssignsCategoryToRoom()
        {
            var service = new RoomService();
            var room = service.CreateRoom("Admin", "conn-1", "Países");
            Assert.Equal("Países", room.Category);
        }

        [Fact]
        public void CreateRoom_WithoutCategory_DefaultsToColores()
        {
            var service = new RoomService();
            var room = service.CreateRoom("Admin", "conn-1");
            Assert.Equal("Colores", room.Category);
        }

        [Fact]
        public void CreateRoom_WithAnimalesCategory_AssignsAnimales()
        {
            var service = new RoomService();
            var room = service.CreateRoom("Admin", "conn-1", "Animales");
            Assert.Equal("Animales", room.Category);
        }
    }
}
