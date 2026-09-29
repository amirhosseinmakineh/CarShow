namespace CarShow.ApplicationService.Contract.Dtos.Order
{
    public record OrderDto
    {
        public long Id { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string RoleName { get; set; } = string.Empty;
        public string CarName { get; set; } = string.Empty;
        public string CreateOrderDate { get; set; } = string.Empty;
    }
}
