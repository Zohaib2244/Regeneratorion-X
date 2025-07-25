using Unity.Entities;
using Unity.Physics;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Physics.Systems;
using Unity.Transforms;
using Unity.Jobs;

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateBefore(typeof(PhysicsSystemGroup))]
[BurstCompile]
public partial struct VOBExplosionSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var ecbSingleton = SystemAPI.GetSingleton<BeginFixedStepSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        JobHandle jobHandle = state.Dependency;

        foreach (var (explosion, explosionEntity) in SystemAPI.Query<ExplosionRequest>().WithEntityAccess())
        {
            var explosionData = explosion;

            // Schedule the job and assign to jobHandle
            jobHandle = new VOBExplosionJob
            {
                ECB = ecb.AsParallelWriter(),
                Explosion = explosionData
            }.ScheduleParallel(jobHandle);
        }

        // Complete the job before using ECB directly
        jobHandle.Complete();

        foreach (var (explosion, explosionEntity) in SystemAPI.Query<ExplosionRequest>().WithEntityAccess())
        {
            ecb.RemoveComponent<ExplosionRequest>(explosionEntity);
        }

        state.Dependency = jobHandle;
    }

    [BurstCompile]
    public partial struct VOBExplosionJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;
        public ExplosionRequest Explosion;

        void Execute(ref LocalTransform transform, in LocalToWorld localToWorld, in VOBComponent vob, Entity entity, [EntityIndexInQuery] int entityInQueryIndex)
        {
            float3 worldPosition = localToWorld.Position;
            quaternion worldRotation = localToWorld.Rotation;
            float worldScale = transform.Scale;

            float3 epicenter = Explosion.Epicenter;
            float distance = math.distance(worldPosition, epicenter);

            ECB.SetComponent(entityInQueryIndex, entity, new Parent { Value = Entity.Null });

            ECB.SetComponent(entityInQueryIndex, entity, new LocalTransform
            {
                Position = worldPosition,
                Rotation = worldRotation,
                Scale = worldScale
            });

            float3 direction = math.normalize(worldPosition - epicenter);
            float forceAmount = Explosion.Force * (1f - (distance / Explosion.Radius));
            float3 velocity = direction * forceAmount;
            float3 angularVelocity = direction * Explosion.RotationAmount;

            var physicsMass = new PhysicsMass
            {
                Transform = RigidTransform.identity,
                InverseMass = 1f,
                InverseInertia = new float3(1f),
                AngularExpansionFactor = 0f
            };
            ECB.AddComponent(entityInQueryIndex, entity, physicsMass);
            ECB.AddComponent(entityInQueryIndex, entity, new PhysicsGravityFactor { Value = 1f });
            ECB.AddComponent(entityInQueryIndex, entity, new PhysicsVelocity
            {
                Linear = velocity,
                Angular = angularVelocity
            });
            ECB.AddComponent(entityInQueryIndex, entity, new VOBExplodedTag());
        }
    }
}