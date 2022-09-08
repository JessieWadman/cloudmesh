import * as cdk from 'aws-cdk-lib';
import { RemovalPolicy } from 'aws-cdk-lib';
import { Vpc } from 'aws-cdk-lib/aws-ec2';
import { Cluster } from 'aws-cdk-lib/aws-ecs';
import { Effect, PolicyDocument, PolicyStatement, Role, ServicePrincipal } from 'aws-cdk-lib/aws-iam';
import { BlockPublicAccess, Bucket, BucketEncryption } from 'aws-cdk-lib/aws-s3';
import { Queue } from 'aws-cdk-lib/aws-sqs';
import { Construct } from 'constructs';
import { CloudMesh } from './cloudmesh';
import { dotNetCode, DotNetLambda } from './dotnet-lambda';
import { DotNetTask, dotNetTaskCode } from './dotnet-task';

export class Example1Stack extends cdk.Stack {
  constructor(scope: Construct, id: string, props?: cdk.StackProps) {
    super(scope, id, props);

    const vpc = Vpc.fromLookup(this, "VPC", {
      isDefault: false,
      tags: {
        'aws:cloudformation:logical-id': 'vpc0'
      }
    });

    const cloudMesh = new CloudMesh(this, 'CloudMesh', {
      cloudMapNamespaceName: 'example1.cloudmesh',
      cloudMapNamespaceDescription: 'Example cloud mesh deployment',
      vpc
    });

    // ------ Sample queue
    const demoQueue = new Queue(this, 'DemoQueue', { });
    cloudMesh.addQueue('demo', demoQueue);


    // ------ Sample file area storage
    const storageBucket = new Bucket(this, 'SomeBlobs', {
      autoDeleteObjects: true,
      removalPolicy: RemovalPolicy.DESTROY,
      blockPublicAccess: BlockPublicAccess.BLOCK_ALL,
      encryption: BucketEncryption.S3_MANAGED
    });
    cloudMesh.addBucket('SomeBlobs', storageBucket);


    // ------ Order service
    const orderService = new DotNetLambda(this, 'OrderService', {
      description: 'Order service',
      code: dotNetCode.buildProject('../src/OrderService', 'bootstrap', 'x64') // arm64 lambdas are not supported in all regions.
    });
    cloudMesh.addLambdaService(orderService.handler, ['IFulfillmentService', 'IOrderPlacementService']);    


    // ------ ECS cluster
    const spawnTasksRole = new Role(this, 'ClusterSpawnTasksRole', {
        assumedBy: new ServicePrincipal('ecs-tasks.amazonaws.com'),
        inlinePolicies: {
            logging: new PolicyDocument({
              statements: [
                new PolicyStatement({
                  effect: Effect.ALLOW,
                  resources: ['*'],
                  actions: [ 'logs:*' ]
                })
              ]
            }),
            ecrRead: new PolicyDocument({
                statements: [
                    new PolicyStatement({
                        effect: Effect.ALLOW,
                        resources: ['*'],
                        actions: [
                            "ecr:GetAuthorizationToken",
                            "ecr:BatchCheckLayerAvailability",
                            "ecr:GetDownloadUrlForLayer",
                            "ecr:BatchGetImage"
                        ]
                    })
                ]
            })
        }
    });

    const ecsCluster = new Cluster(this, 'Cluster', {
      enableFargateCapacityProviders: true,
      vpc
    });


    // ------ Cart service and actors in ECS cluster
    const cartServices = new DotNetTask(this, 'CartService', {
      cloudMesh,
      code: dotNetTaskCode.buildProject('../src/CartServices', undefined, 'x64'),
      description: 'CartService sample',
      ecsCluster,
      ecsClusterExecutionRole: spawnTasksRole,
      repositoryName: 'emample1/cart-service',
      actorInterfaceTypes: ['ICart', 'IProduct'],
      serviceInterfaceTypes: ['ICartService'],
      desiredCount: 1,
      maxHealthyPercent: 300,
      minHealthyPercent: 50
    });

    orderService.handler.grantInvoke(cartServices.role);
  }
}
