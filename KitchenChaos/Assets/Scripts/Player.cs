using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

//Player类

public class Player : MonoBehaviour, IKitchenObjectParent {

    public static Player Instance { get; private set; } //声明一个静态属性，用来保存Player的唯一实例

    public event EventHandler OnPickedSomething;
    public event EventHandler<OnSelectedCounterChasngedEventArgs> OnSelectedCounterChanged; //选择Counter事件
    public class OnSelectedCounterChasngedEventArgs : EventArgs {
        public BaseCounter selectedCounter;
    }


    [SerializeField] private float walkSpeed = 7f; //可在Unity中修改测试玩家速度且私有
    [SerializeField] private float runSpeed = 10.5f;
    [SerializeField] private GameInput gameInput;
    [SerializeField] private LayerMask countersLayerMask;
    [SerializeField] private Transform kitchenObjectHoldPoint;

    private bool isWalking; //动画状态判定
    private bool isRunning;  //加入跑步效果

    private Vector3 lastInteractDir;
    private BaseCounter selectedCounter;
    private KitchenObject kitchenObject;

    private void Awake() {
        if (Instance != null) {
            Debug.LogError("There is more than one Player instance");
        }
        Instance = this;
    }

    private void Start() {
        gameInput.OnInteractAction += GameInput_OnInteractAction;
        gameInput.OnInteractAlternateAction += GameInput_OnInteractAlternateAction;
    }

    private void GameInput_OnInteractAlternateAction(object sender, EventArgs e) {
        if (!KitchenGameManager.Instance.IsGamePlaying()) return;

        if (selectedCounter != null) {
            selectedCounter.InteractAlternate(this);
        }
    }

    private void GameInput_OnInteractAction(object sender, System.EventArgs e) {
        if (!KitchenGameManager.Instance.IsGamePlaying()) return;

        if (selectedCounter != null) {
            selectedCounter.Interact(this);
        }
    }

    private void Update() {
        HandleMovement();
        HandleInteractions();
    }

    public bool IsWalking() {
        return isWalking;
    }
    public bool IsRunning() {
        return isRunning;
    }


    private void HandleInteractions()    //交互管理，判断哪个是被选择的柜台的逻辑
    {
        Vector2 inputVector = gameInput.GetMovementVectorNormalized();

        Vector3 moveDir = new Vector3(inputVector.x, 0f, inputVector.y);

        if (moveDir != Vector3.zero) {
            lastInteractDir = moveDir;  //最后一次移动的方向向量
        }

        float interactDistance = 2f;
        if (Physics.Raycast(transform.position, lastInteractDir, out RaycastHit raycastHit, interactDistance, countersLayerMask)) {
            if (raycastHit.transform.TryGetComponent(out BaseCounter baseCounter)) {
                //有ClearCounter
                if (baseCounter != selectedCounter) {
                    SetSelectedCounter(baseCounter);
                }
            }
            else {
                SetSelectedCounter(null);
            }
        }
        else {
            SetSelectedCounter(null);
        }

    }

    private void HandleMovement()    //移动管理
    {
        if (!KitchenGameManager.Instance.IsGamePlaying()) return;

        Vector2 inputVector = gameInput.GetMovementVectorNormalized();

        Vector3 moveDir = new Vector3(inputVector.x, 0f, inputVector.y);

        isWalking = moveDir != Vector3.zero;      //玩家状态判断
        isRunning = gameInput.IsRunPressed() && isWalking;

        float playerSpeed = isRunning ? runSpeed : walkSpeed;   //确定玩家真实速度,用于碰撞检测
        float moveDistance = playerSpeed * Time.deltaTime;
        float skinWidth = 0.05f;  //安全缓冲
        float playerHeight = 2f;
        float playerRadius = .7f;
        float castRadius = playerRadius - skinWidth;
        bool canMove = !Physics.CapsuleCast(transform.position, transform.position + (Vector3.up * playerHeight), castRadius, moveDir, moveDistance);

        if (!canMove) {
            //说明对于moveDir方向上无法移动
            //需要尝试此时x和z方向是否可以移动
            Vector3 moveDirX = new Vector3(moveDir.x, 0, 0).normalized;
            canMove = (moveDir.x < -.5f || moveDir.x > +.5f) && !Physics.CapsuleCast(transform.position, transform.position + (Vector3.up * playerHeight), castRadius, moveDirX, moveDistance);
            //moveDir.x != 0 即防止它等于0的情况下导致角色moveDir = 0从而玩家卡死
            if (canMove) {
                //如果对于x方向可以移动
                moveDir = moveDirX;
            }
            else {
                //如果对于x方向不可移动
                //需要尝试z方向是否可以移动
                Vector3 moveDirZ = new Vector3(0, 0, moveDir.z).normalized;
                canMove = (moveDir.z < -.5f || moveDir.z > +.5f) && !Physics.CapsuleCast(transform.position, transform.position + (Vector3.up * playerHeight), castRadius, moveDirZ, moveDistance);
                if (canMove) {
                    //如果对于z方向可以移动
                    moveDir = moveDirZ;
                }
                else {
                    //判断发现最后也不可移动，结束
                }
            }
        }

        if (canMove) {
            transform.position += moveDir * moveDistance;
        }

        float rotateSpeed = 30f;
        if (isWalking) {
            transform.forward = Vector3.Slerp(transform.forward, -moveDir, Time.deltaTime * rotateSpeed);//旋转效果 
        }
    }


    private void SetSelectedCounter(BaseCounter selectedCounter) {
        this.selectedCounter = selectedCounter;         //设置被选择Counter，触发事件，通知所有订阅者

        OnSelectedCounterChanged?.Invoke(this, new OnSelectedCounterChasngedEventArgs {
            selectedCounter = selectedCounter
        });
    }

    public Transform GetKitchenObjectFollowTransform() {
        return kitchenObjectHoldPoint;
    }

    public void SetKitchenObject(KitchenObject kitchenObject) {
        this.kitchenObject = kitchenObject;

        if (kitchenObject != null) {
            OnPickedSomething?.Invoke(this, EventArgs.Empty);
        }
    }

    public KitchenObject GetKitchenObject() {
        return kitchenObject;
    }

    public void ClearKitchenObject() {
        kitchenObject = null;
    }

    public bool HasKitchenObject() {
        return kitchenObject != null;
    }
}
