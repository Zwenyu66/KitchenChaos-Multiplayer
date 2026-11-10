using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ClearCounter : BaseCounter {

    [SerializeField] private KitchenObjectSO kitchenObjectSO;


    public override void Interact(Player player) {

        if (!HasKitchenObject()) {
            //没有物体
            if (player.HasKitchenObject()) {
                //玩家拿着物体
                player.GetKitchenObject().SetKitchenObjectParent(this);
            }
            else {
                //玩家没有拿物体
            }
        }
        else {
            //有物体
            if (player.HasKitchenObject()) {
                //玩家拿着物体
                if (player.GetKitchenObject().TryGetPlate(out PlateKitchenObject plateKitchenObject)) {
                    //玩家拿着盘子

                    if (plateKitchenObject.TryAddIngredient(GetKitchenObject().GetKitchenObjectSO())) {
                        GetKitchenObject().DestroySelf();
                    }

                }
                else {
                    //玩家没拿盘子但是拿着其它物体
                    if (GetKitchenObject().TryGetPlate(out plateKitchenObject)) {
                        //此柜台有盘子在上面
                        if (plateKitchenObject.TryAddIngredient(player.GetKitchenObject().GetKitchenObjectSO())) {
                            player.GetKitchenObject().DestroySelf();
                        }
                    }
                }
            }
            else {
                //玩家没拿物体
                GetKitchenObject().SetKitchenObjectParent(player);
            }
        }


    }
}
