using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Play.Common;
using Play.Trading.Service.Dtos;
using Play.Trading.Service.Entities;

namespace Play.Trading.Service.Controllers;

[ApiController]
[Route("store")]
[Authorize]
public class StoreController: ControllerBase
{
    private readonly IRepository<CatalogItem> catalogRepository;
    private readonly IRepository<ApplicationUser> usersRepository;
    private readonly IRepository<InventoryItem> inventoryRepository;

    public StoreController(
        IRepository<InventoryItem> inventoryRepository,
        IRepository<ApplicationUser> usersRepository,
        IRepository<CatalogItem> catalogRepository)
    {
        this.inventoryRepository = inventoryRepository;
        this.usersRepository = usersRepository;
        this.catalogRepository = catalogRepository;
    }

    [HttpGet]
    public async Task<ActionResult<StoreDto>> GetAsync()
    {
        string userId = User.FindFirstValue("sub"); // name of the claims in token
        var catalogItems = await catalogRepository.GetAllAsync();

        var inventoryItems = await inventoryRepository.GetAllAsync(
            item => item.UserId == Guid.Parse(userId));
        var user = await usersRepository.GetAsync(Guid.Parse(userId));

        var storeDto = new StoreDto(
            catalogItems.Select(catalogItem => 
                new StoreItemDto( 
                    catalogItem.Id, 
                    catalogItem.Name, 
                    catalogItem.Description,
                    catalogItem.Price,
                    inventoryItems.FirstOrDefault(
                        x=> x.CatalogItemId == catalogItem.Id)?.Quantity ?? 0)),
                user?.Gil ?? 0
        );

        return Ok(storeDto);
    }
}