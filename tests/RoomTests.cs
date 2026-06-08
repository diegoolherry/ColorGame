using ColorGame.Models;
using Xunit;

namespace ColorGame.Tests
{
    public class RoomTests
    {
        // T1: Room.Category property with default "Colores"
        [Fact]
        public void Room_Category_DefaultsToColores()
        {
            var room = new Room();
            Assert.Equal("Colores", room.Category);
        }

        [Fact]
        public void Room_Category_CanBeSet()
        {
            var room = new Room { Category = "Países" };
            Assert.Equal("Países", room.Category);
        }
    }
}
