#!/usr/bin/env node
import 'source-map-support/register';
import * as cdk from 'aws-cdk-lib';
import { Example1Stack } from '../lib/example1-stack';

const app = new cdk.App();
new Example1Stack(app, 'Example1Stack', {
  description: 'Stack for testing CloudMesh features. It is safe to delete at any time',
  tags: {
    purpose: 'Experimentation',
    safetodelete: 'Yes'
  },
  env: {
		account: process.env.CDK_DEFAULT_ACCOUNT,
		region: process.env.CDK_DEFAULT_REGION,
	}
});